using System;
using System.Drawing;
using System.Collections;
using System.ComponentModel;
using System.Windows.Forms;
using System.Data;
using System.IO;
using System.Xml;

using TCatSysManagerLib;	// Namespace for the System manager library


namespace SysManTest
{
	public class Form1 : System.Windows.Forms.Form
	{
		private System.Windows.Forms.Button btnOpen;
		private System.Windows.Forms.TextBox edConfName;
		private System.Windows.Forms.Button btnBrowseFile;
		private System.Windows.Forms.Button btnNew;
		private System.Windows.Forms.Button btnSaveAs;
		private System.Windows.Forms.Button btnRescanDevices;
		private System.Windows.Forms.TextBox edSaveAs;
		private System.Windows.Forms.ListView lvDevices;
		private System.Windows.Forms.Button btnScanBoxes;
		private System.Windows.Forms.ListView lvBoxes;
		private System.Windows.Forms.Button btnImport;
		private System.Windows.Forms.Button btnExport;
		
        /// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.Container components = null;
		private string productName = "Automation Interface Sample";
		private System.Windows.Forms.ListView lvTerminals;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.Label label3;
		private System.Windows.Forms.TabControl tabControl1;
		private System.Windows.Forms.TabPage tabPage1;
		private System.Windows.Forms.TabPage tabPage2;
		private System.Windows.Forms.TabPage tabPage3;
		private System.Windows.Forms.Button btnAddTerminals;
		private System.Windows.Forms.GroupBox groupBox1;
		private System.Windows.Forms.GroupBox groupBox2;
		private SysManTest.TreeBrowser treeBrowser1;
		private System.Windows.Forms.Button btnUpdate;
		private System.Windows.Forms.Button btnTargetNetID;
		private System.Windows.Forms.TextBox tbNetID;
		private System.Windows.Forms.Button btnRestartSystem;
		private System.Windows.Forms.GroupBox groupBox3;
		private System.Windows.Forms.Label label4;
		private System.Windows.Forms.Label lblAmsNetId;
		
		private string currentProject = "Closed";
        private bool projectOpened = false;

        /// <summary>
        /// Global object for the system manager
        /// </summary>
        TcSysManagerClass systemManager = null;

		/// <summary>
		/// Constructor for the Main Form
		/// </summary>
		public Form1()
		{
			InitializeComponent();
			writeCaption();
			enableDisableControls();
		}

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (components != null)
                {
                    components.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Form load handler
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Form1_Load(object sender, System.EventArgs e)
        {
        }

		#region Windows Form Designer generated code
		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.btnOpen = new System.Windows.Forms.Button();
            this.edConfName = new System.Windows.Forms.TextBox();
            this.btnBrowseFile = new System.Windows.Forms.Button();
            this.btnNew = new System.Windows.Forms.Button();
            this.btnSaveAs = new System.Windows.Forms.Button();
            this.btnRescanDevices = new System.Windows.Forms.Button();
            this.edSaveAs = new System.Windows.Forms.TextBox();
            this.lvDevices = new System.Windows.Forms.ListView();
            this.btnScanBoxes = new System.Windows.Forms.Button();
            this.lvBoxes = new System.Windows.Forms.ListView();
            this.btnImport = new System.Windows.Forms.Button();
            this.btnExport = new System.Windows.Forms.Button();
            this.lvTerminals = new System.Windows.Forms.ListView();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.tabPage2 = new System.Windows.Forms.TabPage();
            this.btnAddTerminals = new System.Windows.Forms.Button();
            this.tabPage3 = new System.Windows.Forms.TabPage();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.tbNetID = new System.Windows.Forms.TextBox();
            this.btnTargetNetID = new System.Windows.Forms.Button();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.btnUpdate = new System.Windows.Forms.Button();
            this.btnRestartSystem = new System.Windows.Forms.Button();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.lblAmsNetId = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.treeBrowser1 = new SysManTest.TreeBrowser();
            this.tabControl1.SuspendLayout();
            this.tabPage1.SuspendLayout();
            this.tabPage2.SuspendLayout();
            this.tabPage3.SuspendLayout();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.groupBox3.SuspendLayout();
            this.SuspendLayout();
            // 
            // btnOpen
            // 
            this.btnOpen.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnOpen.Location = new System.Drawing.Point(16, 61);
            this.btnOpen.Name = "btnOpen";
            this.btnOpen.Size = new System.Drawing.Size(75, 23);
            this.btnOpen.TabIndex = 0;
            this.btnOpen.Text = "Open";
            this.btnOpen.Click += new System.EventHandler(this.btnOpen_Click);
            // 
            // edConfName
            // 
            this.edConfName.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.edConfName.Location = new System.Drawing.Point(97, 63);
            this.edConfName.Name = "edConfName";
            this.edConfName.Size = new System.Drawing.Size(392, 20);
            this.edConfName.TabIndex = 1;
            this.edConfName.TextChanged += new System.EventHandler(this.edConfName_TextChanged);
            // 
            // btnBrowseFile
            // 
            this.btnBrowseFile.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnBrowseFile.Location = new System.Drawing.Point(495, 61);
            this.btnBrowseFile.Name = "btnBrowseFile";
            this.btnBrowseFile.Size = new System.Drawing.Size(29, 23);
            this.btnBrowseFile.TabIndex = 2;
            this.btnBrowseFile.Text = "...";
            this.btnBrowseFile.Click += new System.EventHandler(this.btnBrowseFile_Click);
            // 
            // btnNew
            // 
            this.btnNew.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnNew.Location = new System.Drawing.Point(16, 32);
            this.btnNew.Name = "btnNew";
            this.btnNew.Size = new System.Drawing.Size(75, 23);
            this.btnNew.TabIndex = 4;
            this.btnNew.Text = "New";
            this.btnNew.Click += new System.EventHandler(this.btnNew_Click);
            // 
            // btnSaveAs
            // 
            this.btnSaveAs.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnSaveAs.Location = new System.Drawing.Point(16, 90);
            this.btnSaveAs.Name = "btnSaveAs";
            this.btnSaveAs.Size = new System.Drawing.Size(75, 23);
            this.btnSaveAs.TabIndex = 6;
            this.btnSaveAs.Text = "SaveAs";
            this.btnSaveAs.Click += new System.EventHandler(this.btnSaveAs_Click);
            // 
            // btnRescanDevices
            // 
            this.btnRescanDevices.Location = new System.Drawing.Point(456, 18);
            this.btnRescanDevices.Name = "btnRescanDevices";
            this.btnRescanDevices.Size = new System.Drawing.Size(80, 24);
            this.btnRescanDevices.TabIndex = 7;
            this.btnRescanDevices.Text = "ScanDevices";
            this.btnRescanDevices.Click += new System.EventHandler(this.btnRescanDevices_Click);
            // 
            // edSaveAs
            // 
            this.edSaveAs.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.edSaveAs.Location = new System.Drawing.Point(97, 92);
            this.edSaveAs.Name = "edSaveAs";
            this.edSaveAs.Size = new System.Drawing.Size(392, 20);
            this.edSaveAs.TabIndex = 8;
            // 
            // lvDevices
            // 
            this.lvDevices.Location = new System.Drawing.Point(16, 48);
            this.lvDevices.Name = "lvDevices";
            this.lvDevices.Size = new System.Drawing.Size(520, 104);
            this.lvDevices.TabIndex = 9;
            this.lvDevices.UseCompatibleStateImageBehavior = false;
            this.lvDevices.View = System.Windows.Forms.View.List;
            this.lvDevices.SelectedIndexChanged += new System.EventHandler(this.lvDevices_SelectedIndexChanged);
            // 
            // btnScanBoxes
            // 
            this.btnScanBoxes.Location = new System.Drawing.Point(205, 163);
            this.btnScanBoxes.Name = "btnScanBoxes";
            this.btnScanBoxes.Size = new System.Drawing.Size(75, 23);
            this.btnScanBoxes.TabIndex = 10;
            this.btnScanBoxes.Text = "ScanBoxes";
            this.btnScanBoxes.Click += new System.EventHandler(this.btnScanBoxes_Click);
            // 
            // lvBoxes
            // 
            this.lvBoxes.Location = new System.Drawing.Point(16, 192);
            this.lvBoxes.Name = "lvBoxes";
            this.lvBoxes.Size = new System.Drawing.Size(264, 242);
            this.lvBoxes.TabIndex = 11;
            this.lvBoxes.UseCompatibleStateImageBehavior = false;
            this.lvBoxes.View = System.Windows.Forms.View.List;
            this.lvBoxes.SelectedIndexChanged += new System.EventHandler(this.lvBoxes_SelectedIndexChanged);
            // 
            // btnImport
            // 
            this.btnImport.Location = new System.Drawing.Point(24, 32);
            this.btnImport.Name = "btnImport";
            this.btnImport.Size = new System.Drawing.Size(75, 23);
            this.btnImport.TabIndex = 12;
            this.btnImport.Text = "Import";
            this.btnImport.Click += new System.EventHandler(this.btnImport_Click);
            // 
            // btnExport
            // 
            this.btnExport.Location = new System.Drawing.Point(24, 64);
            this.btnExport.Name = "btnExport";
            this.btnExport.Size = new System.Drawing.Size(75, 23);
            this.btnExport.TabIndex = 13;
            this.btnExport.Text = "Export";
            this.btnExport.Click += new System.EventHandler(this.exportBtn_Click);
            // 
            // lvTerminals
            // 
            this.lvTerminals.Location = new System.Drawing.Point(288, 192);
            this.lvTerminals.Name = "lvTerminals";
            this.lvTerminals.Size = new System.Drawing.Size(248, 242);
            this.lvTerminals.TabIndex = 14;
            this.lvTerminals.UseCompatibleStateImageBehavior = false;
            this.lvTerminals.View = System.Windows.Forms.View.List;
            // 
            // label1
            // 
            this.label1.Location = new System.Drawing.Point(288, 168);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(100, 23);
            this.label1.TabIndex = 15;
            this.label1.Text = "Terminals:";
            // 
            // label2
            // 
            this.label2.Location = new System.Drawing.Point(16, 168);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(100, 23);
            this.label2.TabIndex = 16;
            this.label2.Text = "Boxes:";
            // 
            // label3
            // 
            this.label3.Location = new System.Drawing.Point(16, 24);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(100, 16);
            this.label3.TabIndex = 17;
            this.label3.Text = "Devices:";
            // 
            // tabControl1
            // 
            this.tabControl1.Controls.Add(this.tabPage1);
            this.tabControl1.Controls.Add(this.tabPage2);
            this.tabControl1.Controls.Add(this.tabPage3);
            this.tabControl1.Location = new System.Drawing.Point(8, 264);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(560, 509);
            this.tabControl1.TabIndex = 18;
            // 
            // tabPage1
            // 
            this.tabPage1.Controls.Add(this.btnRescanDevices);
            this.tabPage1.Controls.Add(this.lvDevices);
            this.tabPage1.Controls.Add(this.btnScanBoxes);
            this.tabPage1.Controls.Add(this.lvBoxes);
            this.tabPage1.Controls.Add(this.lvTerminals);
            this.tabPage1.Controls.Add(this.label1);
            this.tabPage1.Controls.Add(this.label2);
            this.tabPage1.Controls.Add(this.label3);
            this.tabPage1.Location = new System.Drawing.Point(4, 22);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Size = new System.Drawing.Size(552, 483);
            this.tabPage1.TabIndex = 0;
            this.tabPage1.Text = "Scan Devices";
            // 
            // tabPage2
            // 
            this.tabPage2.Controls.Add(this.btnAddTerminals);
            this.tabPage2.Location = new System.Drawing.Point(4, 22);
            this.tabPage2.Name = "tabPage2";
            this.tabPage2.Size = new System.Drawing.Size(552, 483);
            this.tabPage2.TabIndex = 1;
            this.tabPage2.Text = "EtherCAT";
            // 
            // btnAddTerminals
            // 
            this.btnAddTerminals.Location = new System.Drawing.Point(32, 32);
            this.btnAddTerminals.Name = "btnAddTerminals";
            this.btnAddTerminals.Size = new System.Drawing.Size(112, 23);
            this.btnAddTerminals.TabIndex = 0;
            this.btnAddTerminals.Text = "AddTerminals";
            this.btnAddTerminals.Click += new System.EventHandler(this.btnAddTerminals_Click);
            // 
            // tabPage3
            // 
            this.tabPage3.Controls.Add(this.btnImport);
            this.tabPage3.Controls.Add(this.btnExport);
            this.tabPage3.Location = new System.Drawing.Point(4, 22);
            this.tabPage3.Name = "tabPage3";
            this.tabPage3.Size = new System.Drawing.Size(552, 483);
            this.tabPage3.TabIndex = 2;
            this.tabPage3.Text = "Import/Export";
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.edConfName);
            this.groupBox1.Controls.Add(this.btnOpen);
            this.groupBox1.Controls.Add(this.edSaveAs);
            this.groupBox1.Controls.Add(this.btnNew);
            this.groupBox1.Controls.Add(this.btnSaveAs);
            this.groupBox1.Controls.Add(this.btnBrowseFile);
            this.groupBox1.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.groupBox1.Location = new System.Drawing.Point(8, 112);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(560, 144);
            this.groupBox1.TabIndex = 19;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "System Manager project";
            // 
            // tbNetID
            // 
            this.tbNetID.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.tbNetID.Location = new System.Drawing.Point(792, 16);
            this.tbNetID.Name = "tbNetID";
            this.tbNetID.Size = new System.Drawing.Size(100, 20);
            this.tbNetID.TabIndex = 10;
            this.tbNetID.TextChanged += new System.EventHandler(this.tbNetID_TextChanged);
            // 
            // btnTargetNetID
            // 
            this.btnTargetNetID.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnTargetNetID.Location = new System.Drawing.Point(682, 14);
            this.btnTargetNetID.Name = "btnTargetNetID";
            this.btnTargetNetID.Size = new System.Drawing.Size(104, 23);
            this.btnTargetNetID.TabIndex = 9;
            this.btnTargetNetID.Text = "Set Target Net ID";
            this.btnTargetNetID.Click += new System.EventHandler(this.btnTargetNetID_Click);
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.btnUpdate);
            this.groupBox2.Controls.Add(this.treeBrowser1);
            this.groupBox2.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.groupBox2.Location = new System.Drawing.Point(576, 112);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(368, 661);
            this.groupBox2.TabIndex = 0;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Configuration";
            // 
            // btnUpdate
            // 
            this.btnUpdate.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnUpdate.Location = new System.Drawing.Point(152, 624);
            this.btnUpdate.Name = "btnUpdate";
            this.btnUpdate.Size = new System.Drawing.Size(75, 23);
            this.btnUpdate.TabIndex = 1;
            this.btnUpdate.Text = "Update";
            this.btnUpdate.Click += new System.EventHandler(this.btnUpdate_Click);
            // 
            // btnRestartSystem
            // 
            this.btnRestartSystem.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnRestartSystem.Location = new System.Drawing.Point(682, 43);
            this.btnRestartSystem.Name = "btnRestartSystem";
            this.btnRestartSystem.Size = new System.Drawing.Size(104, 23);
            this.btnRestartSystem.TabIndex = 11;
            this.btnRestartSystem.Text = "Restart System";
            this.btnRestartSystem.Click += new System.EventHandler(this.btnRestartSystem_Click);
            // 
            // groupBox3
            // 
            this.groupBox3.Controls.Add(this.lblAmsNetId);
            this.groupBox3.Controls.Add(this.label4);
            this.groupBox3.Controls.Add(this.btnRestartSystem);
            this.groupBox3.Controls.Add(this.btnTargetNetID);
            this.groupBox3.Controls.Add(this.tbNetID);
            this.groupBox3.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.groupBox3.Location = new System.Drawing.Point(8, 16);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(936, 80);
            this.groupBox3.TabIndex = 20;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "Target System";
            // 
            // lblAmsNetId
            // 
            this.lblAmsNetId.Location = new System.Drawing.Point(94, 19);
            this.lblAmsNetId.Name = "lblAmsNetId";
            this.lblAmsNetId.Size = new System.Drawing.Size(139, 23);
            this.lblAmsNetId.TabIndex = 13;
            // 
            // label4
            // 
            this.label4.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label4.Location = new System.Drawing.Point(24, 24);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(67, 23);
            this.label4.TabIndex = 12;
            this.label4.Text = "AmsNetID:";
            // 
            // treeBrowser1
            // 
            this.treeBrowser1.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.treeBrowser1.Location = new System.Drawing.Point(16, 24);
            this.treeBrowser1.Name = "treeBrowser1";
            this.treeBrowser1.Size = new System.Drawing.Size(336, 584);
            this.treeBrowser1.TabIndex = 0;
            // 
            // Form1
            // 
            this.AutoScaleBaseSize = new System.Drawing.Size(5, 13);
            this.ClientSize = new System.Drawing.Size(960, 806);
            this.Controls.Add(this.groupBox3);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.tabControl1);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MaximumSize = new System.Drawing.Size(976, 844);
            this.MinimumSize = new System.Drawing.Size(976, 844);
            this.Name = "Form1";
            this.Text = "Automation Interface Sample";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.tabControl1.ResumeLayout(false);
            this.tabPage1.ResumeLayout(false);
            this.tabPage2.ResumeLayout(false);
            this.tabPage3.ResumeLayout(false);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox3.ResumeLayout(false);
            this.groupBox3.PerformLayout();
            this.ResumeLayout(false);

		}
		#endregion

		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main() 
		{
			Application.Run(new Form1());
		}

		/// <summary>
		/// Creation of the System Manager object
		/// </summary>
		private void createSystemManager()
		{
			systemManager = new TcSysManagerClass(); // Instantiating the System Manager during Form_Load event
			treeBrowser1.Init(systemManager);
		}

		/// <summary>
		/// Browse File Click handler
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void btnBrowseFile_Click(object sender, System.EventArgs e)
		{
			OpenFileDialog openFileDialog = new OpenFileDialog();
			openFileDialog.InitialDirectory = "c:\\" ;
			openFileDialog.Filter = "System Manager files (*.tsm)|*.tsm|System manager files (*.wsm)|*.wsm" ;
			openFileDialog.FilterIndex = 1 ;
			openFileDialog.RestoreDirectory = true ;

			if (openFileDialog.ShowDialog() == DialogResult.OK)
			{
				this.edConfName.Text = openFileDialog.FileName;
			}
			enableDisableControls();
		}

		/// <summary>
		/// Gets the Project Open flag
		/// </summary>
		public bool ProjectOpened
		{
			get { return projectOpened; }
		}

		/// <summary>
		/// Project close handler
		/// </summary>
		private void OnProjectClose()
		{
			// IMPORTANT! Hands of tree Items
			this.treeBrowser1.Clear();
		}
		
		/// <summary>
		/// Project open handler
		/// </summary>
		/// <param name="projectName"></param>
		private void OnProjectOpened(string projectName)
		{
			projectOpened = true;
			currentProject = projectName;
			writeCaption();
			tbNetID.Text = systemManager.GetTargetNetId();
			this.treeBrowser1.UpdateTree();

			enableDisableControls();
		}

		#region Button Click handlers
				
		/// <summary>
		/// Open Project Button click handler
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void btnOpen_Click(object sender, System.EventArgs e)
		{
			Cursor oldCursor = this.Cursor;
			this.Cursor = Cursors.WaitCursor;

			try
			{
				if (systemManager == null)
					createSystemManager();

				OnProjectClose();
				systemManager.OpenConfiguration(this.edConfName.Text);
				OnProjectOpened(this.edConfName.Text);
			}
			catch (OutOfMemoryException mEx)
			{
				systemManager = null;
				string message = string.Format("{0}\nAnother System Manager Process is blocking the Automation Interface.\nPlease close all System Manager processes!",mEx.Message);
				MessageBox.Show(message,"Error",MessageBoxButtons.OK,MessageBoxIcon.Error);
			}
			catch (UnauthorizedAccessException uEx)
			{
				systemManager = null;
				string message = string.Format("{0}\nAnother System Manager Process is blocking the Automation Interface.\nPlease close all System Manager processes!",uEx.Message);
				MessageBox.Show(message,"Error",MessageBoxButtons.OK,MessageBoxIcon.Error);
			}
			finally
			{
				this.Cursor = oldCursor;
			}
		}

		/// <summary>
		/// New Project Button Click handler
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void btnNew_Click(object sender, System.EventArgs e)
		{
			Cursor oldCursor = this.Cursor;
			this.Cursor = Cursors.WaitCursor;

			try
			{
				if (systemManager == null)
					createSystemManager();

				OnProjectClose();
				systemManager.NewConfiguration();
				OnProjectOpened("UNKNOWN");
			}
			catch (OutOfMemoryException mEx)
			{
				string message = string.Format("{0}\nAnother System Manager Process is blocking the Automation Interface.\nPlease close all System Manager processes!",mEx.Message);
				MessageBox.Show(message,"Error",MessageBoxButtons.OK,MessageBoxIcon.Error);
			}
			catch (UnauthorizedAccessException uEx)
			{
				string message = string.Format("{0}\nAnother System Manager Process is blocking the Automation Interface.\nPlease close all System Manager processes!",uEx.Message);
				MessageBox.Show(message,"Error",MessageBoxButtons.OK,MessageBoxIcon.Error);
			}
			finally
			{
				this.Cursor = oldCursor;
			}
		}
		
		/// <summary>
		/// Save as Button Click handler
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void btnSaveAs_Click(object sender, System.EventArgs e)
		{
			
			Cursor oldCursor = this.Cursor;
			this.Cursor = Cursors.WaitCursor;
			
			try
			{
				systemManager.SaveConfiguration(edSaveAs.Text);
				currentProject = edSaveAs.Text;
				OnProjectOpened(edSaveAs.Text);

			}
			finally
			{
				this.Cursor = oldCursor;
			}
		}

		
		/// <summary>
		/// Rescan Button Click handler
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void btnRescanDevices_Click(object sender, System.EventArgs e)
		{
			Cursor oldCursor = this.Cursor;
			this.Cursor = Cursors.WaitCursor;
			ITcSmTreeItem2 devicesGroup = (ITcSmTreeItem2)systemManager.LookupTreeItem("TIID");

			try
			{
				XmlNodeList list = ScanDevices();

				foreach(XmlNode node in list)
				{
					//					XmlNode descr = node.SelectSingleNode("//Device/AddressInfo/Pnp/DeviceDesc");
					//					string name = descr.InnerText;
					//					
					//					bool found = false;
					//					foreach(ITcSmTreeItem2 child in devicesGroup)
					//					{
					//						if (child.Name == name)
					//							found = true;
					//					}
					//
					//					// Add the device only, when it is not in the Configuration
					//					if (!found)
					AddDevice(node);
				}
				emptyListBoxes();
				listDevices();
			}
			finally
			{
				this.Cursor = oldCursor;
			}
			treeBrowser1.UpdateTree();
		}

		/// <summary>
		/// Scan Boxes Button Click handler
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void btnScanBoxes_Click(object sender, System.EventArgs e)
		{
			Cursor oldCursor = this.Cursor;
			this.Cursor = Cursors.WaitCursor;

			try
			{
				if (lvDevices.SelectedItems != null && lvDevices.SelectedItems.Count > 0)
				{
					ITcSmTreeItem2 rteDevice = (ITcSmTreeItem2)lvDevices.SelectedItems[0].Tag;
					ScanBoxes(rteDevice);
				}
			}
			finally
			{
				this.Cursor = oldCursor;
			}
			treeBrowser1.UpdateTree();
		}

		/// <summary>
		/// Export Button Click handler
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void exportBtn_Click(object sender, System.EventArgs e)
		{
			
			Cursor oldCursor = this.Cursor;
			this.Cursor = Cursors.WaitCursor;

			try
			{
				ITcSmTreeItem2 ret = null;
				ITcSmTreeItem2 devicesGroup = (ITcSmTreeItem2)systemManager.LookupTreeItem("TIID");
			
				ret = (ITcSmTreeItem2)devicesGroup.CreateChild("ExportTestDevice",(int)TCSYSMANAGERDEVICETYPES.TSM_DEV_TYPE_ETHERNET,"",null);
			}
			finally
			{
				this.Cursor = oldCursor;
			}
		}

		/// <summary>
		/// Import Button Click handler
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void btnImport_Click(object sender, System.EventArgs e)
		{
			Cursor oldCursor = this.Cursor;
			this.Cursor = Cursors.WaitCursor;

			try
			{
				// TODO 
			}
			finally
			{
				this.Cursor = oldCursor;
			}

		}

		/// <summary>
		/// Update Button click handler
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void btnUpdate_Click(object sender, System.EventArgs e)
		{
			Cursor oldCursor = this.Cursor;
			this.Cursor = Cursors.WaitCursor;

			try
			{
				treeBrowser1.UpdateTree();
			}
			finally
			{
				this.Cursor = oldCursor;
			}
		}
		
		/// <summary>
		/// Click handler of the "AddTerminals" Button
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void btnAddTerminals_Click(object sender, System.EventArgs e)
		{
			ITcSmTreeItem2 device = CreateEtherCATDevice("EtherCATDevice1");
			
			// Adding an EK1100 (VendorId 2, Product Code 0x044c2c52) 
			ITcSmTreeItem2 box = CreateEtherCATBox(device,"Box1",2,0x044c2c52,0);	// EL2004
			ITcSmTreeItem2 coupler = CreateEtherCATBox(device,"CX1100_0004",2,0x044c6032,4);	// CX_1100_0004
			// Adding an EL2004 (VendorId 2, Product Code 0x7d43052)
			ITcSmTreeItem2 terminal = CreateEtherCATBox(device,"Terminal1",2,0x7d43052,0);	// In EtherCAT Terminals are also Boxes and must be added on the device!

			treeBrowser1.UpdateTree();
		}

		#endregion

		/// <summary>
		/// Scans the Devices and returns an XML Node list describing the devices
		/// </summary>
		/// <returns></returns>
		private XmlNodeList ScanDevices()
		{
			ITcSmTreeItem2 devicesGroup = (ITcSmTreeItem2)systemManager.LookupTreeItem("TIID");
			string devicesXml = devicesGroup.ProduceXml(false);

			XmlDocument dom = new XmlDocument();
			dom.LoadXml(devicesXml);

			XmlNodeList ret = dom.SelectNodes("TreeItem/DeviceGrpDef/FoundDevices/Device");
			return ret;
		}
		
		/// <summary>
		/// Sets the ScanBoxes flag on the device
		/// </summary>
		/// <param name="device"></param>
		private void ScanBoxes(ITcSmTreeItem2 device)
		{
			if (device.ItemSubType == 0x2d)
			{	
				string message = "The selected device is a Virtual Ethernet Device!\nA Scan Device results in a reconfiguration of all connected boxes, what usually should not be done in the whole Ehternet subnet!";
				MessageBox.Show(message,"Ignoring ScanBoxes",MessageBoxButtons.OK,MessageBoxIcon.Warning);
			}
			else
			{
				SetScanBox(device,true);
				lvBoxes.Clear();
				listBoxes(device);
				SetScanBox(device,false);
			}
		}
		
		/// <summary>
		/// Fills the Boxes of a specified device into the "boxes" List box
		/// </summary>
		/// <param name="device"></param>
		private void listBoxes(ITcSmTreeItem2 device)
		{
			foreach (ITcSmTreeItem2 child in device)
			{
				if (child.ItemType == (int)TREEITEMTYPES.TREEITEMTYPE_BOX) // Checking for the Item type box
				{
					ListViewItem item = lvBoxes.Items.Add(child.Name);
					item.Tag = child;
				}
			}
		}
		
		/// <summary>
		/// Fills the devices into the "devices" list box.
		/// </summary>
		private void listDevices()
		{
			ITcSmTreeItem2 devicesGroup = (ITcSmTreeItem2)systemManager.LookupTreeItem("TIID");
			foreach (ITcSmTreeItem2 device in devicesGroup)
			{
				if (device.ItemType == (int)TREEITEMTYPES.TREEITEMTYPE_DEVICE) // Device (2)
				{
					ListViewItem item = lvDevices.Items.Add(device.Name);
					item.Tag = device;
				}
			}
		}
		
		/// <summary>
		/// Fills the Terminals of a specified box into the "Terminals" list box
		/// </summary>
		/// <param name="box"></param>
		private void listTerminals(ITcSmTreeItem2 box)
		{
			foreach (ITcSmTreeItem2 boxChild in box)
			{
				if (boxChild.ItemType == (int)TREEITEMTYPES.TREEITEMTYPE_TERM) // Terminal (6)
				{
					ListViewItem item = lvTerminals.Items.Add(boxChild.Name);
					item.Tag = boxChild;
				}
			}
		}

		/// <summary>
		/// Sets / Resets the Scan Box Flag on a device
		/// </summary>
		/// <param name="device"></param>
		/// <param name="scan"></param>
		private void SetScanBox(ITcSmTreeItem2 device, bool scan)
		{
			try
			{
				int s = scan ? 1 : 0;
				string xml = string.Format(System.Globalization.CultureInfo.InvariantCulture,"<TreeItem><DeviceDef><ScanBoxes>{0}</ScanBoxes></DeviceDef></TreeItem>",s);
				device.ConsumeXml(xml);

			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message,"Error",MessageBoxButtons.OK,MessageBoxIcon.Error);
			}
		}

		/// <summary>
		/// Adds a RT-Ethernet device to the configuration
		/// </summary>
		/// <param name="name"></param>
		/// <returns></returns>
		private TCatSysManagerLib.ITcSmTreeItem2 AddDevice(XmlNode xmlNode)
		{
			XmlDocument dom = new XmlDocument(); // Creates a new XML DOM

			XmlNode treeItem = dom.CreateElement("TreeItem");	// Tree item tag as root
			dom.AppendChild(treeItem);							// Insert it in do,
			XmlNode deviceDef = dom.CreateElement("DeviceDef");	// Create Device Element
			string test = xmlNode.InnerXml;						// This text dump contains the complete XML Description
				
			// Read the type of the device
			
			XmlNode deviceNameNode = xmlNode.SelectSingleNode("ItemSubTypeName");
			XmlNode subTypeNode = xmlNode.SelectSingleNode("ItemSubType");
			int subType = int.Parse(subTypeNode.InnerXml);
			string deviceName = deviceNameNode.InnerXml;

			XmlNode addressInfo = xmlNode.SelectSingleNode("AddressInfo");
			
			if (addressInfo != null)
			{
				//XmlNode descr = xmlNode.SelectSingleNode("//Device/AddressInfo/Pnp/DeviceDesc");	// Check for a Plug and Play device
				//string test2 = descr.InnerXml;
				
				deviceDef.InnerXml = addressInfo.OuterXml;
				treeItem.AppendChild(deviceDef);
			}

			ITcSmTreeItem2 ret = null;
			ITcSmTreeItem2 devicesGroup = (ITcSmTreeItem2)systemManager.LookupTreeItem("TIID");
			
			
			ret = (ITcSmTreeItem2)devicesGroup.CreateChild(deviceName,subType,"",null);

			//string test2 = dom.OuterXml;
			ret.ConsumeXml(dom.OuterXml);
			
			return ret;
		}

		
		/// <summary>
		/// Updates the Visual Elements of the Form
		/// </summary>
		private void enableDisableControls()
		{
			btnOpen.Enabled = File.Exists(this.edConfName.Text);

			bool netIDChanged = false;

			if (ProjectOpened)
			{
				string currentNetId = systemManager.GetTargetNetId();
				netIDChanged = (currentNetId != tbNetID.Text);
				this.lblAmsNetId.Text = currentNetId;
			}

			btnSaveAs.Enabled = ProjectOpened;
			btnRescanDevices.Enabled = ProjectOpened;
			btnAddTerminals.Enabled = ProjectOpened;
			btnUpdate.Enabled = ProjectOpened;
			btnTargetNetID.Enabled = netIDChanged;
			btnRestartSystem.Enabled = ProjectOpened;
			tbNetID.Enabled = ProjectOpened;
			
			ITcSmTreeItem2 rteDevice = null;
			if (ProjectOpened && lvDevices.SelectedItems != null && lvDevices.SelectedItems.Count > 0)
			{
				rteDevice = (ITcSmTreeItem2)lvDevices.SelectedItems[0].Tag;
			}
			
			btnScanBoxes.Enabled = rteDevice != null;
			btnImport.Enabled = false;
			btnExport.Enabled = false;
		}

		
		/// <summary>
		/// Text Changed handler of the project name edit control
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void edConfName_TextChanged(object sender, System.EventArgs e)
		{
			btnOpen.Enabled = File.Exists(this.edConfName.Text);
		}

		/// <summary>
		/// Selected index handler of the "Boxes" List box
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void lvBoxes_SelectedIndexChanged(object sender, System.EventArgs e)
		{
			lvTerminals.Clear();
			
			if (lvBoxes.SelectedItems != null && lvBoxes.SelectedItems.Count > 0)
			{
				ListViewItem item = lvBoxes.SelectedItems[0];

				if (item != null)
				{
					ITcSmTreeItem2 box = (ITcSmTreeItem2)item.Tag;
					listTerminals(box);
				}
			}
			enableDisableControls();
		}

		/// <summary>
		/// Selected index changed handler of the "Devices" List box
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void lvDevices_SelectedIndexChanged(object sender, System.EventArgs e)
		{
			lvBoxes.Items.Clear();
			lvTerminals.Items.Clear();

			if (lvDevices.SelectedItems != null && lvDevices.SelectedItems.Count > 0)
			{
				ListViewItem item = lvDevices.SelectedItems[0];

				if (item != null)
				{
					ITcSmTreeItem2 device = (ITcSmTreeItem2)item.Tag;
					listBoxes(device);
				}
			}
			enableDisableControls();
		}

		/// <summary>
		/// Creates an EtherCAT Device
		/// </summary>
		/// <param name="name"></param>
		/// <returns></returns>
		private ITcSmTreeItem2 CreateEtherCATDevice(string name)
		{
			ITcSmTreeItem2 devicesGroup = (ITcSmTreeItem2)systemManager.LookupTreeItem("TIID");
			ITcSmTreeItem2 etherCatDevice = (ITcSmTreeItem2)devicesGroup.CreateChild(name,94,string.Empty,null);
			return etherCatDevice;
		}

		/// <summary>
		/// Creates an EtherCAT Box
		/// </summary>
		/// <param name="device"></param>
		/// <param name="name"></param>
		/// <param name="vendorId"></param>
		/// <param name="productCode"></param>
		/// <returns></returns>
		private ITcSmTreeItem2 CreateEtherCATBox(ITcSmTreeItem2 device, string name, int vendorId, int productCode, int revisionNumber)
		{
			ITcSmTreeItem2 box = null;
			
			// This structure is necessary as additional information
			int[] vInfo = new int[4];
			vInfo[0] = vendorId;
			vInfo[1] = productCode;
			vInfo[2] = revisionNumber;
			vInfo[3] = 0;
			
			// SubId EtherCAT Box = 9099
			box = (ITcSmTreeItem2)device.CreateChild(name,9099,string.Empty,vInfo);
			return box;
		}

		private void btnTargetNetID_Click(object sender, System.EventArgs e)
		{
			Cursor oldCursor = this.Cursor;
			this.Cursor = Cursors.WaitCursor;

			try
			{
				systemManager.SetTargetNetId(tbNetID.Text);
				enableDisableControls();
			}
			catch(Exception ex)
			{
				MessageBox.Show(ex.Message);
			}
			finally
			{
				this.Cursor = oldCursor;
			}
		}

		private void tbNetID_TextChanged(object sender, System.EventArgs e)
		{
			enableDisableControls();
		}

		private void btnRestartSystem_Click(object sender, System.EventArgs e)
		{
			Cursor oldCursor = this.Cursor;
			this.Cursor = Cursors.WaitCursor;

			try
			{
				systemManager.StartRestartTwinCAT();
			}
			catch(Exception ex)
			{
				MessageBox.Show(ex.Message);
			}
			finally
			{
				this.Cursor = oldCursor;
			}
		}

        private void emptyListBoxes()
        {
            lvDevices.Items.Clear();
            lvBoxes.Items.Clear();
            lvTerminals.Items.Clear();
        }

        /// <summary>
        /// Sets the Caption string of the main form
        /// </summary>
        private void writeCaption()
        {
            this.Text = string.Format("{0} ({1})", productName, currentProject);
        }

        /// <summary>
        /// Gets the name of the currently opened project
        /// </summary>
        public string CurrentProject
        {
            get { return currentProject; }
        }
	}
}
