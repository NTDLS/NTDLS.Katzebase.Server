using ICSharpCode.AvalonEdit;
using Katzebase.Library.Exceptions;
using Katzebase.Library.Payloads;
using Katzebase.UI.Classes;
using Katzebase.UI.Properties;
using System.Data;
using System.Diagnostics;

namespace Katzebase.UI
{
    public partial class FormStudio : Form
    {
        private int _executionExceptionCount = 0;
        private bool _timerTicking = false;
        private bool _isDoubleClick = false;
        private bool _firstShown = true;
        private readonly EditorFactory? _editorFactory = null;
        private readonly ImageList _treeImages = new ImageList();
        private TextEditor? _outputEditor;
        private readonly System.Windows.Forms.Timer _toolbarSyncTimer = new();
        private bool _scriptExecuting = false;

        internal Project? CurrentProject { get; set; }

        public FormStudio()
        {
            InitializeComponent();
            _editorFactory = new EditorFactory(this, this.tabControlBody);
        }

        public FormStudio(string projectFile)
        {
            InitializeComponent();
            _editorFactory = new EditorFactory(this, this.tabControlBody);
            CurrentProject = new Project(projectFile);
        }

        private void FormStudio_Load(object sender, EventArgs e)
        {
            treeViewProject.Dock = DockStyle.Fill;
            splitContainerProject.Dock = DockStyle.Fill;
            splitContainerMacros.Dock = DockStyle.Fill;
            tabControlBody.Dock = DockStyle.Fill;
            treeViewMacros.Dock = DockStyle.Fill;

            _treeImages.ColorDepth = ColorDepth.Depth32Bit;
            _treeImages.Images.Add("Folder", Resources.Folder);
            _treeImages.Images.Add("Script", Resources.Script);
            _treeImages.Images.Add("Project", Resources.Project);
            _treeImages.Images.Add("Assets", Resources.Assets);
            _treeImages.Images.Add("Asset", Resources.Asset);
            _treeImages.Images.Add("Note", Resources.Note);
            _treeImages.Images.Add("Workloads", Resources.Workloads);
            _treeImages.Images.Add("Workload", Resources.Workload);
            treeViewProject.ImageList = _treeImages;
            treeViewProject.LabelEdit = true;

            treeViewProject.NodeMouseDoubleClick += TreeViewProject_NodeMouseDoubleClick;
            treeViewProject.BeforeCollapse += TreeViewProject_BeforeCollapse;
            treeViewProject.BeforeExpand += TreeViewProject_BeforeExpand;
            treeViewProject.MouseDown += TreeViewProject_MouseDown;
            treeViewProject.NodeMouseClick += TreeViewProject_NodeMouseClick;
            treeViewProject.AfterLabelEdit += TreeViewProject_AfterLabelEdit;
            treeViewProject.BeforeLabelEdit += TreeViewProject_BeforeLabelEdit;
            treeViewProject.KeyUp += TreeViewProject_KeyUp;

            treeViewMacros.ShowNodeToolTips = true;
            treeViewMacros.Nodes.AddRange(FormUtility.GetMacroTreeNoded());
            treeViewMacros.ItemDrag += TreeViewMacros_ItemDrag;

            _outputEditor = EditorFactory.CreateGeneric();

            var host = new System.Windows.Forms.Integration.ElementHost
            {
                Dock = DockStyle.Fill,
                Child = _outputEditor
            };
            tabPagePreview.Controls.Add(host);

            this.Shown += FormStudio_Shown;
            this.FormClosing += FormStudio_FormClosing;

            tabControlBody.MouseUp += TabControlBody_MouseUp;

            splitContainerOutput.Panel2Collapsed = true; //For now, we just hide the bottom panel since we dont really do debugging.

            _toolbarSyncTimer.Tick += _toolbarSyncTimer_Tick;
            _toolbarSyncTimer.Interval = 250;
            _toolbarSyncTimer.Start();
        }

        private void _toolbarSyncTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                lock (this)
                {
                    if (_timerTicking)
                    {
                        return;
                    }
                    _timerTicking = true;
                }

                SyncToolbarAndMenuStates();

                var macroNode = GetMacroAssetsNode();
                var projectNode = GetProjectAssetsNode();

                if (projectNode == null)
                {
                    macroNode.Nodes.Clear();
                }
                else
                {
                    bool expand = false;

                    var macroList = BuildMacroNodeTextList(macroNode);
                    var projectList = BuildMacroNodeTextList(projectNode);

                    var nodesToRemove = macroList.Except(projectList);
                    var nodesToAdd = projectList.Except(macroList);

                    foreach (var nodeText in nodesToRemove)
                    {
                        FormUtility.FindNode(macroNode, nodeText)?.Remove();
                    }

                    expand = macroList.Count == 0;

                    foreach (var nodeText in nodesToAdd)
                    {
                        macroNode.Nodes.Add(FormUtility.MacroNode(nodeText, $"::DS({nodeText})", $"Gets a random record from the {nodeText} asset file."));
                    }
                    if (nodesToAdd.Count() > 0)
                    {
                        FormUtility.SortChildNodes(macroNode);
                    }

                    if (expand)
                    {
                        macroNode.Expand();
                    }
                }

                _timerTicking = false;
            }
            catch { }
        }

        private void SyncToolbarAndMenuStates()
        {
            var tab = CurrentTabInfo();

            bool isProjectOpen = (CurrentProject?.IsLoaded == true);
            bool isTabOpen = (tab?.Tab != null);
            bool isTextSelected = (tab?.Tab != null) && (tab?.Editor?.SelectionLength > 0);

            toolStripButtonCloseCurrentTab.Enabled = isTabOpen;
            toolStripButtonCopy.Enabled = isTextSelected;
            toolStripButtonCut.Enabled = isTextSelected;
            toolStripButtonFind.Enabled = isTabOpen;
            toolStripButtonPaste.Enabled = isTabOpen;
            toolStripButtonRedo.Enabled = isTabOpen;
            toolStripButtonReplace.Enabled = isTabOpen;
            toolStripButtonExecuteProject.Enabled = isProjectOpen;
            toolStripButtonExecuteScript.Enabled = isTabOpen && !_scriptExecuting;
            toolStripButtonUndo.Enabled = isTabOpen;
            toolStripButtonPreview.Enabled = isTabOpen;

            toolStripButtonDecreaseIndent.Enabled = isTextSelected;
            toolStripButtonIncreaseIndent.Enabled = isTextSelected;

            toolStripButtonSave.Enabled = isProjectOpen;
            toolStripButtonSaveAll.Enabled = isProjectOpen;
            toolStripButtonSnippets.Enabled = isTabOpen;
        }

        #region Form events.

        private void FormStudio_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (CloseAllTabs() == false)
            {
                e.Cancel = true;
            }
        }

        private void FormStudio_Shown(object? sender, EventArgs e)
        {
            if (_firstShown == false)
            {
                return;
            }

            if (_firstShown)
            {
                _firstShown = false;
            }

            if (File.Exists(CurrentProject?.ProjectFile))
            {
                LoadProject(CurrentProject.ProjectFile);
            }
            else if (Preferences.Instance.ShowWelcome && CurrentProject?.IsLoaded != true)
            {
                using (var form = new FormWelcome())
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        if (form.FormalResult == FormWelcome.Result.CreateNew)
                        {
                            CreateNewProject();
                        }
                        else if (form.FormalResult == FormWelcome.Result.OpenExisting)
                        {
                            BrowseForProject();
                        }
                        else if (form.FormalResult == FormWelcome.Result.OpenRecent)
                        {
                            LoadProject(form.SelectedProject);
                        }
                    }
                }
            }

            SyncToolbarAndMenuStates();
        }

        #endregion

        #region Project Treeview Shenanigans.

        private List<ProjectTreeNode> GetFlatProjectNodes()
        {
            List<ProjectTreeNode> flatList = new();

            foreach (var node in treeViewProject.Nodes.Cast<ProjectTreeNode>())
            {
                flatList.Add(node);
                FlattendTreeViewNodes(ref flatList, node);
            }


            return flatList;
        }

        private void FlattendTreeViewNodes(ref List<ProjectTreeNode> flatList, ProjectTreeNode parent)
        {
            foreach (var node in parent.Nodes.Cast<ProjectTreeNode>())
            {
                flatList.Add(node);
                FlattendTreeViewNodes(ref flatList, node);
            }
        }

        private ProjectTreeNode? GetProjectAssetsNode(ProjectTreeNode startNode)
        {
            foreach (var node in startNode.Nodes.Cast<ProjectTreeNode>())
            {
                if (node.Text == "Assets")
                {
                    return node;
                }
                var result = GetProjectAssetsNode(node);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private void TreeViewProject_KeyUp(object? sender, KeyEventArgs e)
        {
            var node = treeViewProject.SelectedNode as ProjectTreeNode;
            if (node != null)
            {
                if (e.KeyCode == Keys.F2)
                {
                    node.BeginEdit();
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    if (node.NodeType == Constants.ProjectNodeType.Script || node.NodeType == Constants.ProjectNodeType.Asset
                        || node.NodeType == Constants.ProjectNodeType.Note || node.NodeType == Constants.ProjectNodeType.Workloads
                        || node.NodeType == Constants.ProjectNodeType.Workload)
                    {
                        AddOrSelectTab(node);
                    }
                }
            }
        }

        private void TreeViewProject_BeforeLabelEdit(object? sender, NodeLabelEditEventArgs e)
        {
            if (e.Node is not ProjectTreeNode node)
            {
                throw new Exception("Invlaid node type.");
            }

            if (node.NodeType != Constants.ProjectNodeType.Asset
               && node.NodeType != Constants.ProjectNodeType.Script
               && node.NodeType != Constants.ProjectNodeType.Workload
               && node.NodeType != Constants.ProjectNodeType.Note)
            {
                e.CancelEdit = true;
            }
        }

        private void TreeViewProject_AfterLabelEdit(object? sender, NodeLabelEditEventArgs e)
        {
            if (e.Node is not ProjectTreeNode node)
            {
                throw new Exception("Invlaid node type.");
            }

            string newLabel = e.Label ?? string.Empty;
            if (newLabel == string.Empty)
            {
                e.CancelEdit = true;
                return;
            }

            if (node.NodeType == Constants.ProjectNodeType.Asset
                || node.NodeType == Constants.ProjectNodeType.Script
                || node.NodeType == Constants.ProjectNodeType.Note)
            {
                string newExtension = Path.GetExtension(newLabel);
                if (newExtension == string.Empty)
                {
                    string oldExtension = Path.GetExtension(node.Text);
                    if (oldExtension != string.Empty)
                    {
                        newLabel = $"{newLabel}{oldExtension}";
                    }
                }

                var openTab = FindTabByFilePath(node.FullFilePath);
                string newFullPath = node.Rename(newLabel);

                node.Text = newLabel;

                if (openTab != null)
                {
                    openTab.Text = Path.GetFileName(newFullPath);
                    openTab.Node = node;
                }

                e.CancelEdit = true; //Because we may need to add an extension, we changed the label manually, no need to allow the event to do it.
            }
            else if (node.NodeType == Constants.ProjectNodeType.Workload)
            {
                string newFullPath = node.Rename(newLabel);

                foreach (ProjectTreeNode child in node.Nodes)
                {
                    var openTab = FindTabByFilePath(child.FullFilePath);

                    child.FullFilePath = Path.Combine(newFullPath, Path.GetFileName(child.FullFilePath));

                    if (openTab != null)
                    {
                        openTab.Text = Path.GetFileName(newFullPath);
                        openTab.Node = child;
                    }
                }
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private void TreeViewProject_NodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            //Tested: Good
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            var popupMenu = new ContextMenuStrip();
            treeViewProject.SelectedNode = e.Node;

            popupMenu.ItemClicked += PopupMenu_ItemClicked;

            var node = e.Node as ProjectTreeNode;
            if (node == null)
            {
                throw new Exception("Invalid node type.");
            }

            popupMenu.Tag = e.Node as ProjectTreeNode;

            if (node.NodeType == Constants.ProjectNodeType.Project)
            {
                popupMenu.Items.Add("Refresh", FormUtility.TransparentImage(Resources.ToolFind));
            }

            if (node.NodeType == Constants.ProjectNodeType.Workloads
                || node.NodeType == Constants.ProjectNodeType.Workload
                || node.NodeType == Constants.ProjectNodeType.Asset
                || node.NodeType == Constants.ProjectNodeType.Script
                )
            {
                popupMenu.Items.Add("Edit", FormUtility.TransparentImage(Resources.ToolProjectPanel));
                popupMenu.Items.Add("-");
            }

            if (node.NodeType == Constants.ProjectNodeType.Workloads)
            {
                popupMenu.Items.Add("New Workload", FormUtility.TransparentImage(Resources.Workload));
                popupMenu.Items.Add("-");
            }
            if (node.NodeType == Constants.ProjectNodeType.Workload)
            {
                popupMenu.Items.Add("New Script", FormUtility.TransparentImage(Resources.Workload));
                popupMenu.Items.Add("-");
            }
            if (node.NodeType == Constants.ProjectNodeType.Assets)
            {
                popupMenu.Items.Add("New Asset", FormUtility.TransparentImage(Resources.Asset));
                popupMenu.Items.Add("-");
            }
            if (node.NodeType == Constants.ProjectNodeType.Project)
            {
                popupMenu.Items.Add("New Note", FormUtility.TransparentImage(Resources.Asset));
                popupMenu.Items.Add("-");
            }

            if (node.NodeType == Constants.ProjectNodeType.Asset
                || node.NodeType == Constants.ProjectNodeType.Script
                || node.NodeType == Constants.ProjectNodeType.Workload
                || node.NodeType == Constants.ProjectNodeType.Note)
            {
                popupMenu.Items.Add("Delete", FormUtility.TransparentImage(Resources.ToolDelete));
                popupMenu.Items.Add("Rename", FormUtility.TransparentImage(Resources.ToolReplace));
                popupMenu.Items.Add("-");
            }

            popupMenu.Items.Add("Open containing folder", FormUtility.TransparentImage(Resources.ToolOpenFile));
            popupMenu.Show(treeViewProject, e.Location);
        }

        private void PopupMenu_ItemClicked(object? sender, ToolStripItemClickedEventArgs e)
        {
            var menuStrip = sender as ContextMenuStrip;
            if (menuStrip == null)
            {
                throw new Exception("Menu should never be null.");
            }

            menuStrip.Close();

            if (menuStrip.Tag == null)
            {
                throw new Exception("Tag should never be null.");
            }

            var node = (menuStrip.Tag) as ProjectTreeNode;
            if (node == null)
            {
                throw new Exception("Node should never be null.");
            }

            if (e.ClickedItem?.Text == "Refresh")
            {
                if (CurrentProject != null && CurrentProject.IsLoaded)
                {
                    if (CloseAllTabs() == true)
                    {
                        LoadProject(CurrentProject.ProjectFile);
                    }
                }
            }
            else if (e.ClickedItem?.Text == "Delete")
            {
                var messageBoxResult = MessageBox.Show($"Delete {node.Text} to the recycle bin?", $"Delete {node.NodeType}?", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (messageBoxResult == DialogResult.Yes)
                {
                    var tab = FindTabByFilePath(node.FullFilePath);
                    if (tab != null)
                    {
                        if (CloseTab(tab) == false)
                        {
                            return;
                        }
                    }

                    FormUtility.Recycle(node.FullFilePath);
                    node.Remove();
                }
            }
            else if (e.ClickedItem?.Text == "Edit")
            {
                AddOrSelectTab(node);
            }
            else if (e.ClickedItem?.Text == "New Script")
            {
                var newNode = node.AddScriptNode();
                if (node.IsExpanded == false) node.Expand();
                treeViewProject.SelectedNode = newNode;
                newNode.BeginEdit();
            }
            else if (e.ClickedItem?.Text == "New Asset")
            {
                var newNode = node.AddAssetNode();
                if (node.IsExpanded == false) node.Expand();
                treeViewProject.SelectedNode = newNode;
                newNode.BeginEdit();
            }
            else if (e.ClickedItem?.Text == "New Note")
            {
                var newNode = node.AddNoteNode();
                if (node.IsExpanded == false) node.Expand();
                treeViewProject.SelectedNode = newNode;
                newNode.BeginEdit();
            }
            else if (e.ClickedItem?.Text == "New Workload")
            {
                var newNode = node.AddWorkloadNode();
                if (node.IsExpanded == false) node.Expand();
                treeViewProject.SelectedNode = newNode;
                newNode.BeginEdit();
            }
            else if (e.ClickedItem?.Text == "Rename")
            {
                node.BeginEdit();
            }
            else if (e.ClickedItem?.Text == "Open containing folder")
            {
                var directory = node.FullFilePath;

                if (Directory.Exists(node.FullFilePath) == false)
                {
                    directory = Path.GetDirectoryName(directory);
                }

                Process.Start(new ProcessStartInfo()
                {
                    FileName = directory,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
        }

        #endregion

        #region Project Treeview Events.

        private void TreeViewProject_NodeMouseDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            var node = (ProjectTreeNode)e.Node;
            if (node.NodeType == Constants.ProjectNodeType.Script
                || node.NodeType == Constants.ProjectNodeType.Asset
                || node.NodeType == Constants.ProjectNodeType.Workload
                || node.NodeType == Constants.ProjectNodeType.Workloads
                || node.NodeType == Constants.ProjectNodeType.Note)
            {
                AddOrSelectTab(node);
            }
        }

        private void TreeViewProject_MouseDown(object? sender, MouseEventArgs e)
        {
            _isDoubleClick = e.Clicks > 1;
        }

        private void TreeViewProject_BeforeExpand(object? sender, TreeViewCancelEventArgs e)
        {
            if (_isDoubleClick && e.Action == TreeViewAction.Expand)
                e.Cancel = true;
        }

        private void TreeViewProject_BeforeCollapse(object? sender, TreeViewCancelEventArgs e)
        {
            if (_isDoubleClick && e.Action == TreeViewAction.Collapse)
                e.Cancel = true;
        }

        #endregion

        #region Macros Treeview Bullshit.

        private TreeNode GetMacroAssetsNode()
        {
            foreach (var node in treeViewMacros.Nodes.Cast<TreeNode>())
            {
                if (node.Text == "Assets")
                {
                    return node;
                }
            }
            throw new Exception("Assets node was not found.");
        }

        private List<string> BuildMacroNodeTextList(TreeNode root)
        {
            var list = new List<string>();
            foreach (var node in root.Nodes.Cast<TreeNode>())
            {
                list.Add(Path.GetFileNameWithoutExtension(node.Text));
            }
            return list;
        }

        private ProjectTreeNode? GetProjectAssetsNode()
        {
            foreach (var node in treeViewProject.Nodes.Cast<ProjectTreeNode>())
            {
                var result = GetProjectAssetsNode(node);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        private void TreeViewMacros_ItemDrag(object? sender, ItemDragEventArgs e)
        {
            if (e.Item != null)
            {
                DoDragDrop(e.Item, DragDropEffects.All);
            }
        }

        #endregion

        #region Body Tab Magic.

        private void TabControlBody_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                var clickedTab = GetClickedTab(e.Location);
                if (clickedTab == null)
                {
                    return;
                }

                var popupMenu = new ContextMenuStrip();
                popupMenu.ItemClicked += popupMenu_tabControlScripts_MouseUp_ItemClicked;

                popupMenu.Tag = clickedTab;

                popupMenu.Items.Add("Close", FormUtility.TransparentImage(Properties.Resources.ToolCloseFile));
                popupMenu.Items.Add("-");
                popupMenu.Items.Add("Close all but this", FormUtility.TransparentImage(Properties.Resources.ToolCloseFile));
                popupMenu.Items.Add("Close all", FormUtility.TransparentImage(Properties.Resources.ToolCloseFile));
                popupMenu.Items.Add("-");
                popupMenu.Items.Add("Find in project", FormUtility.TransparentImage(Properties.Resources.ToolFind));
                popupMenu.Items.Add("Open containing folder", FormUtility.TransparentImage(Properties.Resources.ToolOpenFile));
                popupMenu.Show(tabControlBody, e.Location);
            }
        }

        private void popupMenu_tabControlScripts_MouseUp_ItemClicked(object? sender, ToolStripItemClickedEventArgs e)
        {
            var contextMenu = sender as ContextMenuStrip;
            if (contextMenu == null)
            {
                return;
            }

            contextMenu.Hide();

            ToolStripItem? clickedItem = e?.ClickedItem;
            if (clickedItem == null)
            {
                return;
            }

            ProjectTabPage? clickedTab = contextMenu.Tag as ProjectTabPage;
            if (clickedTab == null)
            {
                return;
            }

            if (clickedItem.Text == "Close")
            {
                CloseTab(clickedTab);
            }
            else if (clickedItem.Text == "Open containing folder")
            {
                if (clickedTab.Node != null)
                {
                    var directory = clickedTab.Node.FullFilePath;

                    if (Directory.Exists(clickedTab.Node.FullFilePath) == false)
                    {
                        directory = Path.GetDirectoryName(directory);
                    }

                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = directory,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
            }
            else if (clickedItem.Text == "Find in project")
            {
                clickedTab.Node.EnsureVisible();
                treeViewProject.SelectedNode = clickedTab.Node;
                treeViewProject.Focus();
            }
            else if (clickedItem.Text == "Close all but this")
            {
                var tabsToClose = new List<ProjectTabPage>();

                //Minimize the number of "SelectedIndexChanged" events that get fired.
                //We get a big ol' thread exception when we dont do this. Looks like an internal control exception.
                tabControlBody.SelectedTab = clickedTab;
                System.Windows.Forms.Application.DoEvents(); //Make sure the message pump can actually select the tab before we start closing.

                foreach (var tab in tabControlBody.TabPages.Cast<ProjectTabPage>())
                {
                    if (tab != clickedTab)
                    {
                        tabsToClose.Add(tab);
                    }
                }

                foreach (var tab in tabsToClose)
                {
                    if (CloseTab(tab) == false)
                    {
                        break;
                    }
                }
            }
            else if (clickedItem.Text == "Close all")
            {
                CloseAllTabs();
            }

            //UpdateToolbarButtonStates();
        }

        private ProjectTabPage? GetClickedTab(Point mouseLocation)
        {
            for (int i = 0; i < tabControlBody.TabCount; i++)
            {
                Rectangle r = tabControlBody.GetTabRect(i);
                if (r.Contains(mouseLocation))
                {
                    return (ProjectTabPage)tabControlBody.TabPages[i];
                }
            }
            return null;
        }

        private void AddOrSelectTab(ProjectTreeNode node)
        {
            var tabPage = FindTabByFilePath(node.FullFilePath);
            if (tabPage != null)
            {
                tabControlBody.SelectedTab = tabPage;
            }
            else
            {
                AddTab(node);
            }
            SyncToolbarAndMenuStates();
        }

        private ProjectTabPage? FindTabByFilePath(string filePath)
        {
            foreach (var tab in tabControlBody.TabPages.Cast<ProjectTabPage>())
            {
                if (tab.Node.FullFilePath == filePath)
                {
                    return tab;
                }
            }
            return null;
        }

        private void AddTab(ProjectTreeNode node)
        {
            if (_editorFactory != null)
            {
                var editor = _editorFactory.Create(node);
                var tab = new ProjectTabPage(node, editor);

                tab.Controls.Add(new System.Windows.Forms.Integration.ElementHost
                {
                    Dock = DockStyle.Fill,
                    Child = editor
                });
                tabControlBody.TabPages.Add(tab);
                tabControlBody.SelectedTab = tab;
            }
        }


        /// <summary>
        /// Removes a tab, saved or not - no prompting.
        /// </summary>
        /// <param name="tab"></param>
        private void RemoveTab(ProjectTabPage? tab)
        {
            if (tab != null)
            {
                tabControlBody.TabPages.Remove(tab);
            }
            SyncToolbarAndMenuStates();
        }

        bool CloseProject()
        {
            if (CurrentProject != null && CurrentProject.IsLoaded)
            {
                if (CloseAllTabs() == false)
                {
                    return false;
                }

                CurrentProject = new Project();
                treeViewProject.Nodes.Clear();
            }

            return true;
        }

        bool BrowseForProject()
        {
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Katzebase Projects (*.vwgp)|*.vwgp|All files (*.*)|*.*";
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    return LoadProject(openFileDialog.FileName);
                }
            }

            return false;
        }

        bool LoadProject(string projectFile)
        {
            if (CloseProject())
            {
                CurrentProject = Project.Load(projectFile, treeViewProject);

                GetFlatProjectNodes().Where(o => o.NodeType == Constants.ProjectNodeType.Workloads).FirstOrDefault()?.Expand();

                return true;
            }
            return false;
        }


        bool CreateNewProject()
        {
            if (CloseProject())
            {
                using (var form = new FormNewProject())
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        Project.Create(form.FullProjectFilePath);
                        LoadProject(form.FullProjectFilePath);
                        return true;
                    }
                }
            }
            return false;
        }

        bool CloseAllTabs()
        {
            //Minimize the number of "SelectedIndexChanged" events that get fired.
            //We get a big ol' thread exception when we dont do this. Looks like an internal control exception.
            tabControlBody.SelectedIndex = 0;
            System.Windows.Forms.Application.DoEvents(); //Make sure the message pump can actually select the tab before we start closing.

            tabControlBody.SuspendLayout();

            bool result = true;
            while (tabControlBody.TabPages.Count != 0)
            {
                if (!CloseTab(tabControlBody.TabPages[tabControlBody.TabPages.Count - 1] as ProjectTabPage))
                {
                    result = false;
                    break;
                }
            }

            SyncToolbarAndMenuStates();

            tabControlBody.ResumeLayout();

            return result;
        }

        /// <summary>
        /// Usser friendly tab close.
        /// </summary>
        /// <param name="tab"></param>
        private bool CloseTab(ProjectTabPage? tab)
        {
            if (tab != null)
            {
                if (tab.IsSaved == false)
                {
                    var messageBoxResult = MessageBox.Show("Save " + tab.Text + " before closing?", "Save File?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (messageBoxResult == DialogResult.Yes)
                    {
                        tab.Save();
                    }
                    else if (messageBoxResult == DialogResult.No)
                    {
                    }
                    else //Cancel and otherwise.
                    {
                        SyncToolbarAndMenuStates();
                        return false;
                    }
                }

                RemoveTab(tab);
            }

            SyncToolbarAndMenuStates();
            return true;
        }

        private TabInfo? CurrentTabInfo()
        {
            var currentTab = tabControlBody.SelectedTab as ProjectTabPage;
            if (currentTab?.Editor != null)
            {
                var node = (ProjectTreeNode)currentTab.Editor.Tag;
                return new TabInfo(node, currentTab.Editor, currentTab);
            }
            return null;
        }

        #endregion

        #region Toolbar Clicks.

        private void toolStripButtonExecuteProject_Click(object sender, EventArgs e)
        {
            if (CurrentProject == null || CurrentProject.ProjectPath == null)
            {
                return;
            }

            if (MessageBox.Show($"Execute the current project against the database server?", $"Are You sure?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            using (var form = new FormExecute(CurrentProject.ProjectFile))
            {
                form.ShowDialog();
            }
        }

        private void toolStripButtonExecuteScript_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show($"Execute the current script against the database server?", $"Are You sure?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            ExecuteCurrentScriptAsync();
        }

        private void toolStripButtonCloseCurrentTab_Click(object sender, EventArgs e)
        {
            var selection = CurrentTabInfo();
            CloseTab(selection?.Tab);
        }

        private void toolStripButtonSave_Click(object sender, EventArgs e)
        {
            var selection = CurrentTabInfo();
            if (selection == null)
            {
                return;
            }
            selection.Tab.Save();
        }

        private void toolStripButtonSaveAll_Click(object sender, EventArgs e)
        {
            foreach (var tab in tabControlBody.TabPages.Cast<ProjectTabPage>())
            {
                tab.Save();
            }
        }

        private void toolStripButtonNewProject_Click(object sender, EventArgs e)
        {
            CreateNewProject();
        }

        private void toolStripButtonFind_Click(object sender, EventArgs e)
        {
            ShowFind();
        }

        private void toolStripButtonReplace_Click(object sender, EventArgs e)
        {
            ShowReplace();
        }

        private void toolStripButtonRedo_Click(object sender, EventArgs e)
        {
            var tab = CurrentTabInfo();
            tab?.Editor.Redo();
        }

        private void toolStripButtonUndo_Click(object sender, EventArgs e)
        {
            var tab = CurrentTabInfo();
            tab?.Editor.Undo();
        }

        private void toolStripButtonCut_Click(object sender, EventArgs e)
        {
            var tab = CurrentTabInfo();
            tab?.Editor.Cut();
        }

        private void toolStripButtonCopy_Click(object sender, EventArgs e)
        {
            var tab = CurrentTabInfo();
            tab?.Editor.Copy();
        }

        private void toolStripButtonPaste_Click(object sender, EventArgs e)
        {
            var tab = CurrentTabInfo();
            tab?.Editor.Paste();
        }

        private void toolStripButtonIncreaseIndent_Click(object sender, EventArgs e)
        {
            IncreaseCurrentTabIndent();
        }

        public void IncreaseCurrentTabIndent()
        {
            var tab = CurrentTabInfo();
            if (tab != null)
            {
                SendKeys.Send("{TAB}");
            }
        }

        private void toolStripButtonDecreaseIndent_Click(object sender, EventArgs e)
        {
            DecreaseCurrentTabIndent();
        }

        public void DecreaseCurrentTabIndent()
        {
            SendKeys.Send("+({TAB})");
        }

        private void toolStripButtonMacros_Click(object sender, EventArgs e)
        {
            splitContainerMacros.Panel2Collapsed = !splitContainerMacros.Panel2Collapsed;
        }

        private void toolStripButtonProject_Click(object sender, EventArgs e)
        {
            splitContainerProject.Panel1Collapsed = !splitContainerProject.Panel1Collapsed;
        }

        public void ShowReplace()
        {
            var info = CurrentTabInfo();
            if (info != null)
            {
                info.Tab.ReplaceTextForm.ShowDialog();
            }
        }

        public void ShowFind()
        {
            var info = CurrentTabInfo();
            if (info != null)
            {
                info.Tab.FindTextForm.ShowDialog();
            }
        }

        public void FindNext()
        {
            var info = CurrentTabInfo();
            if (info != null)
            {
                if (string.IsNullOrEmpty(info.Tab.FindTextForm.SearchText))
                {
                    info.Tab.FindTextForm.ShowDialog();
                }
                else
                {
                    info.Tab.FindTextForm.FindNext();
                }
            }
        }

        private void Group_OnStatus(WorkloadGroup sender, string text, Color color)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<WorkloadGroup, string, Color>(Group_OnStatus), sender, text, color);
                return;
            }

            AppendToOutput(text, color);
        }

        private void AppentOutputText(string text)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(AppentOutputText), text);
                return;
            }
            if (_outputEditor != null)
            {
                _outputEditor.Text += text;
            }
        }

        private void AppendToOutput(string text, Color color)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, Color>(AppendToOutput), text, color);
                return;
            }

            richTextBoxOutput.SelectionStart = richTextBoxOutput.TextLength;
            richTextBoxOutput.SelectionLength = 0;

            richTextBoxOutput.SelectionColor = color;
            richTextBoxOutput.AppendText($"{text}\r\n");
            richTextBoxOutput.SelectionColor = richTextBoxOutput.ForeColor;

            richTextBoxOutput.SelectionStart = richTextBoxOutput.Text.Length;
            richTextBoxOutput.ScrollToCaret();
        }

        private void Group_OnException(WorkloadGroup sender, KbExceptionBase ex)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<WorkloadGroup, KbExceptionBase>(Group_OnException), sender, ex);
                return;
            }

            _executionExceptionCount++;

            splitContainerOutput.Panel2Collapsed = false;

            if (_outputEditor != null)
            {
                AppendToOutput($"Exception: {ex.Message}\r\n", Color.DarkRed);
            }
        }

        private void toolStripButtonOutput_Click(object sender, EventArgs e)
        {
            splitContainerOutput.Panel2Collapsed = !splitContainerOutput.Panel2Collapsed;
        }

        private void toolStripButtonSnippets_Click(object sender, EventArgs e)
        {
            var tab = CurrentTabInfo();
            if (tab != null)
            {

                using (var form = new FormSnippets())
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        tab.Editor.Document.Insert(tab.Editor.CaretOffset, form.SelectedSnippetText);
                    }
                }
            }
        }

        #endregion

        #region Form Menu.

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selection = CurrentTabInfo();
            if (selection == null)
            {
                return;
            }
            selection.Tab.Save();
        }

        private void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selection = CurrentTabInfo();
            CloseTab(selection?.Tab);
        }

        private void newProjectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CreateNewProject();
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var form = new FormAbout())
            {
                form.ShowDialog();
            }
        }

        private void closeProjectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CloseProject();
        }

        private void closeAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CloseAllTabs();
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void openExistingProjectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            BrowseForProject();
        }

        #endregion

        #region Execute Current Script.

        /// <summary>
        /// This is for actually executing the script against a live database.
        /// </summary>
        private void ExecuteCurrentScriptAsync()
        {
            var tabInfo = CurrentTabInfo();
            if (tabInfo == null)
            {
                return;
            }
            tabInfo.Tab.Save();

            PreExecuteEvent(tabInfo);

            DateTime startTime = DateTime.UtcNow;

            dataGridViewResults.Rows.Clear();
            dataGridViewResults.Columns.Clear();

            string fileName = tabInfo.Tab.Node.FullFilePath;

            Task.Run(() =>
            {
                ExecuteCurrentScriptSync(fileName);
            }).ContinueWith((t) =>
            {
                PostExecuteEvent(tabInfo);
            });
        }

        private void PreExecuteEvent(TabInfo tabInfo)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<TabInfo>(PreExecuteEvent), tabInfo);
                return;
            }

            _scriptExecuting = true;

            richTextBoxOutput.Text = "";
            if (_outputEditor != null) _outputEditor.Text = "";
            _executionExceptionCount = 0;

            splitContainerOutput.Panel2Collapsed = false;
            tabControlOutput.SelectedTab = tabPageOutput;

            if (CurrentProject != null)
            {
                string relPath = Path.GetRelativePath(CurrentProject.ProjectPath, tabInfo.Node.FullFilePath);
                AppendToOutput($"Executing '{relPath}'...", Color.Black);
            }
        }

        private void PostExecuteEvent(TabInfo tabInfo)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<TabInfo>(PostExecuteEvent), tabInfo);
                return;
            }

            splitContainerOutput.Panel2Collapsed = false;

            if (dataGridViewResults.RowCount > 0)
            {
                tabControlOutput.SelectedTab = tabPageResults;
            }
            else
            {
                tabControlOutput.SelectedTab = tabPageOutput;
            }

            _scriptExecuting = false;
        }

        private void ExecuteCurrentScriptSync(string fileName)
        {
            WorkloadGroup group = new WorkloadGroup();

            try
            {
                group.OnException += Group_OnException;
                group.OnStatus += Group_OnStatus;

                if (_outputEditor != null && CurrentProject != null)
                {
                    AppentOutputText("debug text");
                    DateTime startTime = DateTime.UtcNow;

                    var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    AppendToOutput($"Execution completed in {duration:N0}ms.", Color.Black);
                }
            }
            catch (KbExceptionBase ex)
            {
                Group_OnException(group, ex);
            }
            catch (Exception ex)
            {
                Group_OnException(group, new KbExceptionBase(ex.Message));
            }
        }

        private void PopulateResultsGrid(KbQueryResult result)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<KbQueryResult>(PopulateResultsGrid), result);
                return;
            }

            dataGridViewResults.SuspendLayout();

            foreach (var field in result.Fields)
            {
                dataGridViewResults.Columns.Add(field.Name, field.Name);
            }

            int maxRowsToLoad = 100;
            foreach (var row in result.Rows)
            {
                var rowValues = new List<string>();

                for (int fieldIndex = 0; fieldIndex < result.Fields.Count; fieldIndex++)
                {
                    var fieldValue = row.Values[fieldIndex];
                    rowValues.Add(fieldValue ?? string.Empty);
                }

                dataGridViewResults.Rows.Add(rowValues.ToArray());

                maxRowsToLoad--;
                if (maxRowsToLoad <= 0)
                {
                    break;
                }
            }

            dataGridViewResults.ResumeLayout();
        }

        #endregion
    }
}