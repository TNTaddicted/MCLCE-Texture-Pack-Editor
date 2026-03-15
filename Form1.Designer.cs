namespace LegacyConsolePackEditor;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;
    private MenuStrip menuStrip1;
    private ToolStripMenuItem fileToolStripMenuItem;
    private ToolStripMenuItem openArchiveToolStripMenuItem;
    private ToolStripMenuItem openFolderToolStripMenuItem;
    private ToolStripMenuItem downloadTemplateToolStripMenuItem;
    private ToolStripMenuItem loadTemplateToolStripMenuItem;
    private ToolStripMenuItem recentItemsToolStripMenuItem;
    private ToolStripMenuItem saveArchiveToolStripMenuItem;
    private ToolStripMenuItem exitToolStripMenuItem;
    private SplitContainer splitContainer1;
    private TreeView treeViewArchive;
    private TabControl tabControlMain;
    private TabPage tabPagePck;
    private ToolStrip toolStripPck;
    private ToolStripButton toolStripButtonPckExtract;
    private ToolStripButton toolStripButtonPckReplace;
    private ToolStripButton toolStripButtonPckOpen;
    private ToolStripButton toolStripButtonPckDelete;
    private ToolStrip toolStripSwf;
    private ToolStripButton toolStripButtonSwfCapture;
    private SplitContainer splitContainerPck;
    private TreeView treeViewPck;
    private SplitContainer splitContainerPckRight;
    private ListView listViewPckAssets;
    private PictureBox pictureBoxPckPreview;
    private Label labelPckInfo;
    private TabPage tabPageSwfEditor;
    private Panel panelSwfHost;
    private Panel panelHome;
    private TableLayoutPanel tableLayoutPanelHome;
    private PictureBox pictureBoxHomeLogo;
    private Label labelHomeMessage;
    private StatusStrip statusStrip1;
    private ToolStripStatusLabel toolStripStatusLabel1;
    private ToolStripProgressBar toolStripProgressBar;
    private OpenFileDialog openFileDialog;
    private FolderBrowserDialog folderBrowserDialog;
    private SaveFileDialog saveFileDialog;
    private ContextMenuStrip contextMenuTreeView;
    private ToolStripMenuItem extractToolStripMenuItem;
    private ToolStripMenuItem editSwfToolStripMenuItem;
    private ToolStripMenuItem embedSwfEditorToolStripMenuItem;
    private ToolStripMenuItem reloadSwfToolStripMenuItem;
    private ToolStripMenuItem replaceToolStripMenuItem;
    private ToolStripMenuItem deleteToolStripMenuItem;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        menuStrip1 = new MenuStrip();
        fileToolStripMenuItem = new ToolStripMenuItem();
        openArchiveToolStripMenuItem = new ToolStripMenuItem();
        openFolderToolStripMenuItem = new ToolStripMenuItem();
        downloadTemplateToolStripMenuItem = new ToolStripMenuItem();
        loadTemplateToolStripMenuItem = new ToolStripMenuItem();
        recentItemsToolStripMenuItem = new ToolStripMenuItem();
        saveArchiveToolStripMenuItem = new ToolStripMenuItem();
        exitToolStripMenuItem = new ToolStripMenuItem();
        splitContainer1 = new SplitContainer();
        treeViewArchive = new TreeView();
        tabControlMain = new TabControl();
        tabPagePck = new TabPage();
        toolStripPck = new ToolStrip();
        toolStripButtonPckExtract = new ToolStripButton();
        toolStripButtonPckReplace = new ToolStripButton();
        toolStripButtonPckOpen = new ToolStripButton();
        toolStripButtonPckDelete = new ToolStripButton();
        toolStripSwf = new ToolStrip();
        toolStripButtonSwfCapture = new ToolStripButton();
        splitContainerPck = new SplitContainer();
        treeViewPck = new TreeView();
        splitContainerPckRight = new SplitContainer();
        listViewPckAssets = new ListView();
        pictureBoxPckPreview = new PictureBox();
        labelPckInfo = new Label();
        tabPageSwfEditor = new TabPage();
        panelSwfHost = new Panel();
        panelHome = new Panel();
        tableLayoutPanelHome = new TableLayoutPanel();
        pictureBoxHomeLogo = new PictureBox();
        labelHomeMessage = new Label();
        statusStrip1 = new StatusStrip();
        toolStripStatusLabel1 = new ToolStripStatusLabel();
        openFileDialog = new OpenFileDialog();
        folderBrowserDialog = new FolderBrowserDialog();
        saveFileDialog = new SaveFileDialog();
        contextMenuTreeView = new ContextMenuStrip(components);
        extractToolStripMenuItem = new ToolStripMenuItem();
        editSwfToolStripMenuItem = new ToolStripMenuItem();
        embedSwfEditorToolStripMenuItem = new ToolStripMenuItem();
        reloadSwfToolStripMenuItem = new ToolStripMenuItem();
        replaceToolStripMenuItem = new ToolStripMenuItem();
        deleteToolStripMenuItem = new ToolStripMenuItem();
        
        // menuStrip1
        menuStrip1.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem });
        menuStrip1.Location = new Point(0, 0);
        menuStrip1.Name = "menuStrip1";
        menuStrip1.Size = new Size(1000, 24);
        menuStrip1.TabIndex = 0;
        menuStrip1.Text = "menuStrip1";

        // fileToolStripMenuItem
        fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { openArchiveToolStripMenuItem, openFolderToolStripMenuItem, downloadTemplateToolStripMenuItem, loadTemplateToolStripMenuItem, recentItemsToolStripMenuItem, saveArchiveToolStripMenuItem, new ToolStripSeparator(), embedSwfEditorToolStripMenuItem, new ToolStripSeparator(), exitToolStripMenuItem });
        fileToolStripMenuItem.Name = "fileToolStripMenuItem";
        fileToolStripMenuItem.Text = "&File";

        // openArchiveToolStripMenuItem
        openArchiveToolStripMenuItem.Name = "openArchiveToolStripMenuItem";
        openArchiveToolStripMenuItem.Text = "&Open...";
        openArchiveToolStripMenuItem.Click += openArchiveToolStripMenuItem_Click;

        // openFolderToolStripMenuItem
        openFolderToolStripMenuItem.Name = "openFolderToolStripMenuItem";
        openFolderToolStripMenuItem.Text = "Open &Folder...";
        openFolderToolStripMenuItem.Click += openFolderToolStripMenuItem_Click;

        // downloadTemplateToolStripMenuItem
        downloadTemplateToolStripMenuItem.Name = "downloadTemplateToolStripMenuItem";
        downloadTemplateToolStripMenuItem.Text = "Download Template...";
        downloadTemplateToolStripMenuItem.Click += downloadTemplateToolStripMenuItem_Click;

        // loadTemplateToolStripMenuItem
        loadTemplateToolStripMenuItem.Name = "loadTemplateToolStripMenuItem";
        loadTemplateToolStripMenuItem.Text = "Load new template";
        loadTemplateToolStripMenuItem.Click += loadTemplateToolStripMenuItem_Click;

        // recentItemsToolStripMenuItem
        recentItemsToolStripMenuItem.Name = "recentItemsToolStripMenuItem";
        recentItemsToolStripMenuItem.Text = "Open &Recent";

        // saveArchiveToolStripMenuItem
        saveArchiveToolStripMenuItem.Name = "saveArchiveToolStripMenuItem";
        saveArchiveToolStripMenuItem.Text = "&Save As...";
        saveArchiveToolStripMenuItem.Click += saveArchiveToolStripMenuItem_Click;

        // exitToolStripMenuItem
        exitToolStripMenuItem.Name = "exitToolStripMenuItem";
        exitToolStripMenuItem.Text = "E&xit";
        exitToolStripMenuItem.Click += exitToolStripMenuItem_Click;

        // splitContainer1
        splitContainer1.Dock = DockStyle.Fill;
        splitContainer1.Location = new Point(0, 24);
        splitContainer1.Name = "splitContainer1";
        splitContainer1.Size = new Size(1000, 604);
        splitContainer1.SplitterDistance = 300;
        splitContainer1.TabIndex = 1;

        // treeViewArchive
        treeViewArchive.Dock = DockStyle.Fill;
        treeViewArchive.Location = new Point(0, 0);
        treeViewArchive.Name = "treeViewArchive";
        treeViewArchive.Size = new Size(280, 604);
        treeViewArchive.TabIndex = 0;
        treeViewArchive.AfterSelect += treeViewArchive_AfterSelect;
        treeViewArchive.NodeMouseDoubleClick += treeViewArchive_NodeMouseDoubleClick;
        treeViewArchive.ContextMenuStrip = contextMenuTreeView;
        splitContainer1.Panel1.Controls.Add(treeViewArchive);

        // tabControlMain
        tabControlMain.Dock = DockStyle.Fill;
        tabControlMain.Location = new Point(0, 0);
        tabControlMain.Name = "tabControlMain";
        tabControlMain.SelectedIndex = 0;
        tabControlMain.Size = new Size(716, 604);
        tabControlMain.TabIndex = 0;
        tabControlMain.Visible = false;
        tabControlMain.Controls.Add(tabPagePck);
        tabControlMain.Controls.Add(tabPageSwfEditor);
        splitContainer1.Panel2.Controls.Add(tabControlMain);

        // tabPagePck
        tabPagePck.Controls.Add(splitContainerPckRight);
        tabPagePck.Controls.Add(toolStripPck);
        tabPagePck.Location = new Point(4, 24);
        tabPagePck.Name = "tabPagePck";
        tabPagePck.Padding = new Padding(3);
        tabPagePck.Size = new Size(708, 576);
        tabPagePck.TabIndex = 0;
        tabPagePck.Text = "Workspace";
        tabPagePck.UseVisualStyleBackColor = true;

        // toolStripPck
        toolStripPck.Dock = DockStyle.Top;
        toolStripPck.Items.AddRange(new ToolStripItem[] { toolStripButtonPckExtract, toolStripButtonPckReplace, toolStripButtonPckOpen, toolStripButtonPckDelete });
        toolStripPck.Location = new Point(3, 3);
        toolStripPck.Name = "toolStripPck";
        toolStripPck.Size = new Size(702, 25);
        toolStripPck.TabIndex = 0;
        toolStripPck.GripStyle = ToolStripGripStyle.Hidden;
        toolStripPck.RenderMode = ToolStripRenderMode.System;

        // toolStripButtonPckExtract
        toolStripButtonPckExtract.DisplayStyle = ToolStripItemDisplayStyle.Text;
        toolStripButtonPckExtract.Text = "Extract";
        toolStripButtonPckExtract.Click += toolStripButtonPckExtract_Click;

        // toolStripButtonPckReplace
        toolStripButtonPckReplace.DisplayStyle = ToolStripItemDisplayStyle.Text;
        toolStripButtonPckReplace.Text = "Replace";
        toolStripButtonPckReplace.Click += toolStripButtonPckReplace_Click;

        // toolStripButtonPckOpen
        toolStripButtonPckOpen.DisplayStyle = ToolStripItemDisplayStyle.Text;
        toolStripButtonPckOpen.Text = "Open";
        toolStripButtonPckOpen.Click += toolStripButtonPckOpen_Click;

        // toolStripButtonPckDelete
        toolStripButtonPckDelete.DisplayStyle = ToolStripItemDisplayStyle.Text;
        toolStripButtonPckDelete.Text = "Delete";
        toolStripButtonPckDelete.Click += toolStripButtonPckDelete_Click;

        // toolStripSwf
        toolStripSwf.Dock = DockStyle.Top;
        toolStripSwf.Items.AddRange(new ToolStripItem[] { toolStripButtonSwfCapture });
        toolStripSwf.Location = new Point(3, 3);
        toolStripSwf.Name = "toolStripSwf";
        toolStripSwf.Size = new Size(702, 25);
        toolStripSwf.TabIndex = 0;
        toolStripSwf.GripStyle = ToolStripGripStyle.Hidden;
        toolStripSwf.RenderMode = ToolStripRenderMode.System;

        // toolStripButtonSwfCapture
        toolStripButtonSwfCapture.DisplayStyle = ToolStripItemDisplayStyle.Text;
        toolStripButtonSwfCapture.Text = "Edit Bitmap";
        toolStripButtonSwfCapture.Click += toolStripButtonSwfCapture_Click;

        // splitContainerPck
        splitContainerPck.Dock = DockStyle.Fill;
        splitContainerPck.Location = new Point(3, 28);
        splitContainerPck.Name = "splitContainerPck";
        splitContainerPck.Size = new Size(702, 545);
        splitContainerPck.SplitterDistance = 230;
        splitContainerPck.TabIndex = 1;

        // treeViewPck
        treeViewPck.Dock = DockStyle.Fill;
        treeViewPck.Location = new Point(0, 0);
        treeViewPck.Name = "treeViewPck";
        treeViewPck.Size = new Size(250, 545);
        treeViewPck.TabIndex = 0;
        treeViewPck.AfterSelect += treeViewPck_AfterSelect;
        treeViewPck.NodeMouseDoubleClick += treeViewPck_NodeMouseDoubleClick;

        // splitContainerPckRight
        splitContainerPckRight.Dock = DockStyle.Fill;
        splitContainerPckRight.Location = new Point(0, 0);
        splitContainerPckRight.Name = "splitContainerPckRight";
        splitContainerPckRight.Orientation = Orientation.Vertical;
        splitContainerPckRight.Size = new Size(448, 545);
        splitContainerPckRight.SplitterDistance = 270;
        splitContainerPckRight.TabIndex = 0;

        // listViewPckAssets
        listViewPckAssets.Dock = DockStyle.Fill;
        listViewPckAssets.FullRowSelect = true;
        listViewPckAssets.GridLines = true;
        listViewPckAssets.HideSelection = false;
        listViewPckAssets.Location = new Point(0, 0);
        listViewPckAssets.Name = "listViewPckAssets";
        listViewPckAssets.Size = new Size(448, 340);
        listViewPckAssets.TabIndex = 0;
        listViewPckAssets.UseCompatibleStateImageBehavior = false;
        listViewPckAssets.View = View.Details;
        listViewPckAssets.ShowGroups = false;
        listViewPckAssets.Sorting = SortOrder.Ascending;
        listViewPckAssets.Columns.Add("Asset", 340, HorizontalAlignment.Left);
        listViewPckAssets.Columns.Add("Size", 90, HorizontalAlignment.Right);
        listViewPckAssets.MouseDoubleClick += listViewPckAssets_MouseDoubleClick;
        listViewPckAssets.KeyDown += listViewPckAssets_KeyDown;
        listViewPckAssets.SelectedIndexChanged += listViewPckAssets_SelectedIndexChanged;

        // pictureBoxPckPreview
        pictureBoxPckPreview.Dock = DockStyle.Fill;
        pictureBoxPckPreview.Location = new Point(0, 0);
        pictureBoxPckPreview.Name = "pictureBoxPckPreview";
        pictureBoxPckPreview.Size = new Size(174, 523);
        pictureBoxPckPreview.SizeMode = PictureBoxSizeMode.Normal;
        pictureBoxPckPreview.TabIndex = 0;
        pictureBoxPckPreview.TabStop = false;
        pictureBoxPckPreview.BackColor = Color.White;
        pictureBoxPckPreview.Paint += PictureBoxPckPreview_Paint;

        // labelPckInfo
        labelPckInfo.Dock = DockStyle.Bottom;
        labelPckInfo.Location = new Point(0, 523);
        labelPckInfo.Name = "labelPckInfo";
        labelPckInfo.Padding = new Padding(8, 4, 8, 4);
        labelPckInfo.Size = new Size(174, 22);
        labelPckInfo.TabIndex = 1;
        labelPckInfo.Text = "No selection";
        labelPckInfo.TextAlign = ContentAlignment.MiddleLeft;

        splitContainerPckRight.Panel1.Controls.Add(listViewPckAssets);
        splitContainerPckRight.Panel2.Controls.Add(pictureBoxPckPreview);
        splitContainerPckRight.Panel2.Controls.Add(labelPckInfo);

        // treeViewPck and splitContainerPck are kept for non-visual PCK state but not shown in the layout.

        // tabPageSwfEditor
        tabPageSwfEditor.Controls.Add(panelSwfHost);
        tabPageSwfEditor.Controls.Add(toolStripSwf);
        tabPageSwfEditor.Location = new Point(4, 24);
        tabPageSwfEditor.Name = "tabPageSwfEditor";
        tabPageSwfEditor.Padding = new Padding(3);
        tabPageSwfEditor.Size = new Size(708, 576);
        tabPageSwfEditor.TabIndex = 1;
        tabPageSwfEditor.Text = "SWF Editor";
        tabPageSwfEditor.UseVisualStyleBackColor = true;

        // panelSwfHost
        panelSwfHost.Dock = DockStyle.Fill;
        panelSwfHost.Location = new Point(3, 28);
        panelSwfHost.Name = "panelSwfHost";
        panelSwfHost.Size = new Size(702, 545);
        panelSwfHost.TabIndex = 0;
        panelSwfHost.Visible = true;

        // panelHome
        panelHome.Dock = DockStyle.Fill;
        panelHome.Location = new Point(0, 0);
        panelHome.Name = "panelHome";
        panelHome.Size = new Size(716, 604);
        panelHome.TabIndex = 2;
        panelHome.BackColor = Color.LightGray;
        panelHome.BorderStyle = BorderStyle.FixedSingle;

        tableLayoutPanelHome.ColumnCount = 1;
        tableLayoutPanelHome.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tableLayoutPanelHome.RowCount = 3;
        tableLayoutPanelHome.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        tableLayoutPanelHome.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tableLayoutPanelHome.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        tableLayoutPanelHome.Dock = DockStyle.Fill;
        tableLayoutPanelHome.Padding = new Padding(0, 40, 0, 40);

        pictureBoxHomeLogo.Anchor = AnchorStyles.None;
        pictureBoxHomeLogo.Size = new Size(640, 260);
        pictureBoxHomeLogo.SizeMode = PictureBoxSizeMode.Zoom;
        pictureBoxHomeLogo.Margin = new Padding(0, 0, 0, 16);
        pictureBoxHomeLogo.BackColor = Color.LightGray;

        labelHomeMessage.Anchor = AnchorStyles.None;
        labelHomeMessage.AutoSize = true;
        labelHomeMessage.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point);
        labelHomeMessage.ForeColor = Color.FromArgb(80, 80, 80);
        labelHomeMessage.Text = "Open an ARC/PCK/SWF file to begin";
        labelHomeMessage.TextAlign = ContentAlignment.MiddleCenter;

        tableLayoutPanelHome.Controls.Add(pictureBoxHomeLogo, 0, 1);
        tableLayoutPanelHome.Controls.Add(labelHomeMessage, 0, 2);
        panelHome.Controls.Add(tableLayoutPanelHome);
        splitContainer1.Panel2.Controls.Add(panelHome);

        // statusStrip1
        toolStripProgressBar = new ToolStripProgressBar();

        statusStrip1.Items.AddRange(new ToolStripItem[] { toolStripStatusLabel1, toolStripProgressBar });
        statusStrip1.Location = new Point(0, 628);
        statusStrip1.Name = "statusStrip1";
        statusStrip1.Size = new Size(1000, 22);
        statusStrip1.TabIndex = 2;
        statusStrip1.Text = "statusStrip1";

        // toolStripProgressBar
        toolStripProgressBar.Alignment = ToolStripItemAlignment.Right;
        toolStripProgressBar.Name = "toolStripProgressBar";
        toolStripProgressBar.Size = new Size(180, 16);
        toolStripProgressBar.Visible = false;

        // toolStripStatusLabel1
        toolStripStatusLabel1.Name = "toolStripStatusLabel1";
        toolStripStatusLabel1.Size = new Size(39, 17);
        toolStripStatusLabel1.Text = "Ready";

        // openFileDialog
        openFileDialog.Filter = "Minecraft Archive (*.arc)|*.arc|PCK File (*.pck)|*.pck|SWF File (*.swf)|*.swf|All Files (*.*)|*.*";
        openFileDialog.Multiselect = true;

        // folderBrowserDialog
        folderBrowserDialog.Description = "Open a Minecraft legacy texture pack folder";
        folderBrowserDialog.UseDescriptionForTitle = true;

        // saveFileDialog
        saveFileDialog.Filter = "Minecraft Archive (.arc)|*.arc|All files (*.*)|*.*";

        // contextMenuTreeView
        contextMenuTreeView.Items.AddRange(new ToolStripItem[] { extractToolStripMenuItem, editSwfToolStripMenuItem, embedSwfEditorToolStripMenuItem, reloadSwfToolStripMenuItem, replaceToolStripMenuItem, deleteToolStripMenuItem });
        contextMenuTreeView.Name = "contextMenuTreeView";
        contextMenuTreeView.Size = new Size(153, 134);

        // extractToolStripMenuItem
        extractToolStripMenuItem.Name = "extractToolStripMenuItem";
        extractToolStripMenuItem.Text = "Extract...";
        extractToolStripMenuItem.Click += extractToolStripMenuItem_Click;

        // editSwfToolStripMenuItem
        editSwfToolStripMenuItem.Name = "editSwfToolStripMenuItem";
        editSwfToolStripMenuItem.Text = "Edit SWF...";
        editSwfToolStripMenuItem.Click += editSwfToolStripMenuItem_Click;

        // embedSwfEditorToolStripMenuItem
        embedSwfEditorToolStripMenuItem.Name = "embedSwfEditorToolStripMenuItem";
        embedSwfEditorToolStripMenuItem.Text = "Native SWF bitmap editor";
        embedSwfEditorToolStripMenuItem.CheckOnClick = false;
        embedSwfEditorToolStripMenuItem.Checked = true;
        embedSwfEditorToolStripMenuItem.Click += embedSwfEditorToolStripMenuItem_Click;

        // reloadSwfToolStripMenuItem
        replaceToolStripMenuItem.Name = "replaceToolStripMenuItem";
        replaceToolStripMenuItem.Text = "Replace...";
        replaceToolStripMenuItem.Click += replaceToolStripMenuItem_Click;

        // reloadSwfToolStripMenuItem
        reloadSwfToolStripMenuItem.Name = "reloadSwfToolStripMenuItem";
        reloadSwfToolStripMenuItem.Text = "Reload SWF";
        reloadSwfToolStripMenuItem.Click += ReloadSwfToolStripMenuItem_Click;

        // deleteToolStripMenuItem
        deleteToolStripMenuItem.Name = "deleteToolStripMenuItem";
        deleteToolStripMenuItem.Text = "Delete";
        deleteToolStripMenuItem.Click += deleteToolStripMenuItem_Click;

        // Form1
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1000, 650);
        Controls.Add(splitContainer1);
        Controls.Add(statusStrip1);
        Controls.Add(menuStrip1);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        MainMenuStrip = menuStrip1;
        Name = "Form1";
        Text = "MCLCE Texture Pack Editor";
        AllowDrop = true;
        DragEnter += Form1_DragEnter;
        DragDrop += Form1_DragDrop;
        FormClosing += Form1_FormClosing;
    }

    #endregion
}
