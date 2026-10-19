using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>A real-time task as the project has it (TIRT^&lt;Name&gt;).</summary>
    sealed class RtTaskState
    {
        public string Name { get; }
        public int CycleTime100ns { get; }
        public int Priority { get; }

        public RtTaskState(string name, int cycleTime100ns, int priority)
        {
            Name = name;
            CycleTime100ns = cycleTime100ns;
            Priority = priority;
        }
    }

    /// <summary>A PLC task reference as the project has it (&lt;PLC&gt; Project^&lt;Name&gt;):
    /// the real-time task it is linked to and the programs it calls, in call order.</summary>
    sealed class PlcTaskRefState
    {
        public string Name { get; }
        public string LinkedTask { get; }
        public IReadOnlyList<string> Calls { get; }

        public PlcTaskRefState(string name, string linkedTask, IReadOnlyList<string> calls)
        {
            Name = name;
            LinkedTask = linkedTask;
            Calls = calls;
        }
    }

    enum TaskActionKind
    {
        CreateRtTask,   // TIRT^Name (type 1) + TaskDef
        SetTaskDef,     // cycle time and/or priority of an existing TIRT^Name
        CreateTaskRef,  // PLC task reference (type 621) with its first program call
        SetLinkedTask,  // the PLC task reference points to another real-time task
        AddCall,        // a program call (type 650) under an existing PLC task reference
    }

    sealed class TaskAction
    {
        public TaskActionKind Kind { get; }
        public TaskSpec Task { get; }

        /// <summary>CreateTaskRef: the first call; AddCall: the program to add. Null otherwise.</summary>
        public string Program { get; }

        /// <summary>The report line ("+ task ...", "~ task ...").</summary>
        public string Description { get; }

        public TaskAction(TaskActionKind kind, TaskSpec task, string program, string description)
        {
            Kind = kind;
            Task = task;
            Program = program;
            Description = description;
        }
    }

    sealed class TaskSyncPlan
    {
        public List<TaskAction> Actions { get; } = new List<TaskAction>();

        /// <summary>What the project has and the manifest does not: reported, never deleted.</summary>
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Tasks stage: tasks.xml -> real-time tasks and PLC task references (see TaskManifest).
    /// Proven by the M6a spike (BeckhoffLib tasks/2026-10-16-plc-release, 2026-10-17):
    ///   real-time task:      TIRT.CreateChild(name, 1) + ConsumeXml TaskDef (CycleTime in 100 ns, Priority)
    ///   PLC task reference:  &lt;PLC&gt; Project.CreateChild(name, 621, "", "&lt;program&gt;") — TwinCAT links it
    ///                        to TIRT^name by itself and adds the program call (type 650 child)
    /// Runs after the code sync (the programs must exist) and before the I/O links (a link names
    /// the process image of a task, e.g. "SpectrometerTask Inputs"). Read before write: a second
    /// run changes nothing. Extra tasks and calls are reported, never deleted.
    /// </summary>
    static class TaskSyncEngine
    {
        const int TREEITEMTYPE_TASK = 1;
        const int TREEITEMTYPE_PLCTASK = 621;
        const int TREEITEMTYPE_PLCPROGREF = 650;

        public static string RtTaskPath(string name) => "TIRT^" + name;

        /// <summary>The ConsumeXml text that sets the cycle time and priority of a real-time task.</summary>
        internal static string TaskDefXml(TaskSpec task) => string.Format(
            "<TreeItem><TaskDef><CycleTime>{0}</CycleTime><Priority>{1}</Priority></TaskDef></TreeItem>",
            task.CycleTime100ns, task.Priority);

        /// <summary>Cycle time (100 ns) and priority from a real-time task's ProduceXml.</summary>
        internal static (int CycleTime100ns, int Priority) ReadTaskDef(string produceXml)
        {
            XElement def = XDocument.Parse(produceXml).Root?.Element("TaskDef");
            if (def == null)
                throw new InvalidOperationException("the task XML has no <TaskDef>");
            return ((int)def.Element("CycleTime"), (int)def.Element("Priority"));
        }

        /// <summary>What to change (pure, unit-tested).</summary>
        internal static TaskSyncPlan Plan(TaskManifest desired, IReadOnlyList<RtTaskState> rtTasks, IReadOnlyList<PlcTaskRefState> taskRefs)
        {
            var plan = new TaskSyncPlan();
            var rtByName = rtTasks.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
            var refByName = taskRefs.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

            foreach (TaskSpec task in desired.Tasks)
            {
                string want = string.Format("{0} µs, priority {1}", task.CycleTimeUs, task.Priority);
                if (!rtByName.TryGetValue(task.Name, out RtTaskState rt))
                    plan.Actions.Add(new TaskAction(TaskActionKind.CreateRtTask, task, null,
                        string.Format("+ task     {0} ({1})", RtTaskPath(task.Name), want)));
                else if (rt.CycleTime100ns != task.CycleTime100ns || rt.Priority != task.Priority)
                    plan.Actions.Add(new TaskAction(TaskActionKind.SetTaskDef, task, null,
                        string.Format("~ task     {0}: {1} µs, priority {2} -> {3}", RtTaskPath(task.Name), rt.CycleTime100ns / 10, rt.Priority, want)));

                if (!refByName.TryGetValue(task.Name, out PlcTaskRefState taskRef))
                {
                    plan.Actions.Add(new TaskAction(TaskActionKind.CreateTaskRef, task, task.Calls[0],
                        string.Format("+ plc task {0} -> {1}, calls {2}", task.Name, RtTaskPath(task.Name), task.Calls[0])));
                    foreach (string program in task.Calls.Skip(1))
                        plan.Actions.Add(new TaskAction(TaskActionKind.AddCall, task, program,
                            string.Format("+ call     {0}^{1}", task.Name, program)));
                    continue;
                }

                // Changing TaskDef does not update the PLC's cached .TcTTO timing.
                // Reassign even the same LinkedTask after a timing/priority change;
                // otherwise a saved project fails when reopened for activation.
                bool timingChanged = rt != null && (rt.CycleTime100ns != task.CycleTime100ns || rt.Priority != task.Priority);
                if (timingChanged || !string.Equals(taskRef.LinkedTask, RtTaskPath(task.Name), StringComparison.OrdinalIgnoreCase))
                    plan.Actions.Add(new TaskAction(TaskActionKind.SetLinkedTask, task, null,
                        string.Format("~ plc task {0}: linked to '{1}' -> {2}", task.Name, taskRef.LinkedTask, RtTaskPath(task.Name))));
                var calls = new HashSet<string>(taskRef.Calls, StringComparer.OrdinalIgnoreCase);
                foreach (string program in task.Calls.Where(p => !calls.Contains(p)))
                    plan.Actions.Add(new TaskAction(TaskActionKind.AddCall, task, program,
                        string.Format("+ call     {0}^{1}", task.Name, program)));
                var wanted = new HashSet<string>(task.Calls, StringComparer.OrdinalIgnoreCase);
                foreach (string program in taskRef.Calls.Where(p => !wanted.Contains(p)))
                    plan.Warnings.Add(string.Format("task '{0}' also calls '{1}', which tasks.xml does not list — left unchanged (remove it in TwinCAT if it is wrong).", task.Name, program));
                List<string> present = taskRef.Calls.Where(wanted.Contains).ToList();
                List<string> order = task.Calls.Where(calls.Contains).ToList();
                if (!present.SequenceEqual(order, StringComparer.OrdinalIgnoreCase))
                    plan.Warnings.Add(string.Format("task '{0}' calls {1} in this order: {2} — tasks.xml has {3}; the order is left unchanged.",
                        task.Name, present.Count, string.Join(", ", present), string.Join(", ", order)));
            }

            var desiredNames = new HashSet<string>(desired.Tasks.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
            foreach (RtTaskState rt in rtTasks.Where(t => !desiredNames.Contains(t.Name)))
                plan.Warnings.Add(string.Format("real-time task '{0}' ({1} µs, priority {2}) is not in tasks.xml — left unchanged.", RtTaskPath(rt.Name), rt.CycleTime100ns / 10, rt.Priority));
            foreach (PlcTaskRefState taskRef in taskRefs.Where(t => !desiredNames.Contains(t.Name)))
                plan.Warnings.Add(string.Format("PLC task '{0}' (calls {1}) is not in tasks.xml — left unchanged.",
                    taskRef.Name, taskRef.Calls.Count == 0 ? "nothing" : string.Join(", ", taskRef.Calls)));

            var priorities = desired.Tasks.ToDictionary(t => t.Priority, t => t.Name);
            foreach (RtTaskState rt in rtTasks.Where(t => !desiredNames.Contains(t.Name) && priorities.ContainsKey(t.Priority)))
                plan.Warnings.Add(string.Format("real-time task '{0}' has priority {1}, the same as task '{2}' of tasks.xml.", rt.Name, rt.Priority, priorities[rt.Priority]));
            return plan;
        }

        /// <summary>The tasks of the project: TIRT children of type 1, and the PLC project's
        /// task references (type 621) with their program calls (type 650).</summary>
        public static (List<RtTaskState> RtTasks, List<PlcTaskRefState> TaskRefs) ReadState(ITcSysManager sysManager, string plcProjectRootPath)
        {
            var rtTasks = new List<RtTaskState>();
            foreach (ITcSmTreeItem item in Children(sysManager.LookupTreeItem("TIRT")).Where(c => c.ItemType == TREEITEMTYPE_TASK))
            {
                (int cycle, int priority) = ReadTaskDef(item.ProduceXml(false));
                rtTasks.Add(new RtTaskState(item.Name, cycle, priority));
            }

            var taskRefs = new List<PlcTaskRefState>();
            foreach (ITcSmTreeItem item in Children(sysManager.LookupTreeItem(plcProjectRootPath)).Where(c => c.ItemType == TREEITEMTYPE_PLCTASK))
            {
                List<string> calls = Children(item).Where(c => c.ItemType == TREEITEMTYPE_PLCPROGREF).Select(c => c.Name).ToList();
                taskRefs.Add(new PlcTaskRefState(item.Name, ((ITcPlcTaskReference)item).LinkedTask, calls));
            }
            return (rtTasks, taskRefs);
        }

        /// <summary>Applies the plan, then reads the project again: anything still to do is an
        /// error (TwinCAT did not take a change). Returns the plan that was applied.</summary>
        public static TaskSyncPlan Sync(ITcSysManager sysManager, string plcProjectRootPath, TaskManifest desired)
        {
            (List<RtTaskState> rtTasks, List<PlcTaskRefState> taskRefs) = ReadState(sysManager, plcProjectRootPath);
            TaskSyncPlan plan = Plan(desired, rtTasks, taskRefs);
            if (plan.Actions.Count == 0)
                return plan;

            ITcSmTreeItem tirt = sysManager.LookupTreeItem("TIRT");
            ITcSmTreeItem plcRoot = sysManager.LookupTreeItem(plcProjectRootPath);
            // Real-time tasks first: a new PLC task reference links itself to the task of its name.
            foreach (TaskAction action in plan.Actions.Where(a => a.Kind == TaskActionKind.CreateRtTask || a.Kind == TaskActionKind.SetTaskDef))
            {
                ITcSmTreeItem rt = action.Kind == TaskActionKind.CreateRtTask
                    ? tirt.CreateChild(action.Task.Name, TREEITEMTYPE_TASK, "", null)
                    : sysManager.LookupTreeItem(RtTaskPath(action.Task.Name));
                rt.ConsumeXml(TaskDefXml(action.Task));
            }
            foreach (TaskAction action in plan.Actions.Where(a => a.Kind != TaskActionKind.CreateRtTask && a.Kind != TaskActionKind.SetTaskDef))
            {
                string refPath = plcProjectRootPath + "^" + action.Task.Name;
                switch (action.Kind)
                {
                    case TaskActionKind.CreateTaskRef:
                        ITcSmTreeItem created = plcRoot.CreateChild(action.Task.Name, TREEITEMTYPE_PLCTASK, "", action.Program);
                        var taskRef = (ITcPlcTaskReference)created;
                        if (!string.Equals(taskRef.LinkedTask, RtTaskPath(action.Task.Name), StringComparison.OrdinalIgnoreCase))
                            taskRef.LinkedTask = RtTaskPath(action.Task.Name);
                        break;
                    case TaskActionKind.SetLinkedTask:
                        ((ITcPlcTaskReference)sysManager.LookupTreeItem(refPath)).LinkedTask = RtTaskPath(action.Task.Name);
                        break;
                    case TaskActionKind.AddCall:
                        sysManager.LookupTreeItem(refPath).CreateChild(action.Program, TREEITEMTYPE_PLCPROGREF, "", null);
                        break;
                }
            }

            (rtTasks, taskRefs) = ReadState(sysManager, plcProjectRootPath);
            List<TaskAction> left = Plan(desired, rtTasks, taskRefs).Actions;
            if (left.Count > 0)
                throw new InvalidOperationException("TASKS FAILED — TwinCAT did not take these change(s):\n    "
                    + string.Join("\n    ", left.Select(a => a.Description)));
            return plan;
        }

        static IEnumerable<ITcSmTreeItem> Children(ITcSmTreeItem parent)
        {
            for (int i = 1; i <= parent.ChildCount; i++)
                yield return parent.get_Child(i);
        }
    }
}
