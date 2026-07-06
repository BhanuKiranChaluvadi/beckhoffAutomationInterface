using System;
using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Windows.Forms;

using TCatSysManagerLib;

namespace SysManTest
{
	/// <summary>
	/// System Manager Configuration Browser
	/// </summary>
	public class TreeBrowser : System.Windows.Forms.UserControl
	{
		private System.Windows.Forms.TreeView treeView1;
		/// <summary> 
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.Container components = null;

		public TreeBrowser()
		{
			// This call is required by the Windows.Forms Form Designer.
			InitializeComponent();

			// TODO: Add any initialization after the InitializeComponent call

		}

		/// <summary> 
		/// Clean up any resources being used.
		/// </summary>
		protected override void Dispose( bool disposing )
		{
			if( disposing )
			{
				if(components != null)
				{
					components.Dispose();
				}
			}
			base.Dispose( disposing );
		}

		#region Component Designer generated code
		/// <summary> 
		/// Required method for Designer support - do not modify 
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.treeView1 = new System.Windows.Forms.TreeView();
			this.SuspendLayout();
			// 
			// treeView1
			// 
			this.treeView1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.treeView1.ImageIndex = -1;
			this.treeView1.Location = new System.Drawing.Point(0, 0);
			this.treeView1.Name = "treeView1";
			this.treeView1.SelectedImageIndex = -1;
			this.treeView1.Size = new System.Drawing.Size(472, 728);
			this.treeView1.TabIndex = 0;
			// 
			// TreeBrowser
			// 
			this.Controls.Add(this.treeView1);
			this.Name = "TreeBrowser";
			this.Size = new System.Drawing.Size(472, 728);
			this.ResumeLayout(false);

		}
		#endregion

		/// <summary>
		/// System Manager object
		/// </summary>
		private ITcSysManager3 systemManager = null;
		

		/// <summary>
		/// Initializes the Configuration Browser
		/// </summary>
		/// <param name="systemManager"></param>
		public void Init(ITcSysManager3 systemManager)
		{
			Clear();	// Clear the contents
			this.systemManager = systemManager;
		}

		/// <summary>
		/// Adds a subtree to the browser
		/// </summary>
		/// <param name="browserParent"></param>
		/// <returns></returns>
		private int addSubTree(SystemManagerBrowserItem browserParent)
		{
			int counter = addChilds(browserParent,true);
			return counter;
		}
		
		/// <summary>
		/// Adds childs to the parent
		/// </summary>
		/// <param name="browserParent"></param>
		/// <param name="recurse"></param>
		/// <returns></returns>
		private int addChilds(SystemManagerBrowserItem browserParent, bool recurse)
		{
			ITcSmTreeItem2 sysManParent = browserParent.SysManTreeItem;
			int counter = 0;

			foreach (ITcSmTreeItem2 child in sysManParent)
			{
				SystemManagerBrowserItem browserChild = new SystemManagerBrowserItem(child.Name,child);
				browserParent.Nodes.Add(browserChild);
				counter++;

				if (recurse)
					counter += addChilds(browserChild,true);
			}
			return counter;
		}

		/// <summary>
		/// Adds a root into the browser
		/// </summary>
		/// <param name="sysManRoot"></param>
		/// <returns></returns>
		private SystemManagerBrowserItem addRoot(ITcSmTreeItem2 sysManRoot)
		{
			SystemManagerBrowserItem browserRoot = null;

			if (sysManRoot != null)	// Dependant of the Level of TwinCAT
			{
				browserRoot = new SystemManagerBrowserItem(sysManRoot.Name,sysManRoot);
				treeView1.Nodes.Add(browserRoot);
				addSubTree(browserRoot);
			}
			return browserRoot;
		}

		/// <summary>
		/// Clears the Configuration browser
		/// </summary>
		public void Clear()
		{
			treeView1.Nodes.Clear();
		}

		/// <summary>
		/// Updates the complete content
		/// </summary>
		public void UpdateTree()
		{
			//Cursor old = this.Cursor;
			//treeView1.Cursor = Cursors.WaitCursor;

			try
			{
				if (systemManager != null)
				{
					ITcSmTreeItem2 ioConfiguration = null;
					ITcSmTreeItem2 ioDevices = null;
					ITcSmTreeItem2 systemConfiguration = null;
					ITcSmTreeItem2 additionalTasks = null;
					ITcSmTreeItem2 rtSettings = null;
					ITcSmTreeItem2 plcConfiguration = null;
					ITcSmTreeItem2 ncConfiguration = null;
					ITcSmTreeItem2 cncConfiguration = null;
					ITcSmTreeItem2 camConfiguration = null;
				
					ioConfiguration = (ITcSmTreeItem2)systemManager.LookupTreeItem("TIIC");
					ioDevices = (ITcSmTreeItem2)systemManager.LookupTreeItem("TIID");
					systemConfiguration = (ITcSmTreeItem2)systemManager.LookupTreeItem("TIRC");
					additionalTasks = (ITcSmTreeItem2)systemManager.LookupTreeItem("TIRT");
					rtSettings = (ITcSmTreeItem2)systemManager.LookupTreeItem("TIRS");
					plcConfiguration = (ITcSmTreeItem2)systemManager.LookupTreeItem("TIPC");
					ncConfiguration = (ITcSmTreeItem2)systemManager.LookupTreeItem("TINC");
					cncConfiguration = (ITcSmTreeItem2)systemManager.LookupTreeItem("TICC");
					camConfiguration = (ITcSmTreeItem2)systemManager.LookupTreeItem("TIAC");

					treeView1.Nodes.Clear();

					treeView1.BeginUpdate();
					addRoot(systemConfiguration);
                    //addRoot(plcConfiguration);
                    //addRoot(ncConfiguration);
                    //addRoot(cncConfiguration);
					addRoot(ioConfiguration);
					treeView1.EndUpdate();
				}
			}
			finally
			{
				//treeView1.Cursor = old;
			}
		}
	}
}
