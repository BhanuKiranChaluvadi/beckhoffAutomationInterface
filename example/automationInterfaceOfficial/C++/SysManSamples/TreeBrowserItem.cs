using System;
using System.Windows.Forms;

using TCatSysManagerLib;


namespace SysManTest
{
	/// <summary>
	/// Tree Node item for the System Manager configuration tree
	/// </summary>
	public class SystemManagerBrowserItem : TreeNode
	{
		/// <summary>
		/// Constructs the <see cref="SystemManagerBrowserItem"/>
		/// </summary>
		/// <param name="name"></param>
		/// <param name="systemManagerItem"></param>
		public SystemManagerBrowserItem(string name, ITcSmTreeItem2 systemManagerItem) : base(name)
		{
			this.Tag = systemManagerItem;
		}

		/// <summary>
		/// Referenced System Manager Tree Item
		/// </summary>
		public ITcSmTreeItem2 SysManTreeItem
		{
			get { return (ITcSmTreeItem2)this.Tag; }
		}
	}
}
