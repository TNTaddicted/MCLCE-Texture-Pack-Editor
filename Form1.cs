using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using OMI;
using OMI.Formats.Archive;
using OMI.Formats.Pck;
using OMI.Workers.Archive;
using OMI.Workers.Pck;

namespace LegacyConsolePackEditor
{
    public partial class Form1 : Form
    {
        private enum EditorMode
        {
            None,
            Arc,
            Pck,
        }

        private EditorMode _mode = EditorMode.None;
        private ConsoleArchive? _archive;
        private string? _archivePath;

        private PckFile? _pckFile;
        private string? _pckFilePath;
        private PckFile? _standalonePckFile;
        private string? _standalonePckPath;

        private string? _currentPckKey;

        private readonly ARCFileReader _arcReader = new ARCFileReader();
        private readonly PckFileReader _pckReader = new PckFileReader();

        private readonly Settings _settings;
        private string? _lastSwfTempFile;
        private string? _lastSwfArchiveKey;

        private System.Diagnostics.Process? _swfEditorProcess;
        private IntPtr _swfEditorHandle = IntPtr.Zero;
        private bool _swfResizeHooked;
        private bool _embedSwfEditor = true;
        private System.Windows.Forms.Timer? _swfEmbedMonitorTimer;

        private PictureBox? _pckPreviewBox;
        private System.IO.FileSystemWatcher? _pckTempWatcher;
        private string? _pckTempFile;
        private System.IO.FileSystemWatcher? _swfTempWatcher;
        private DateTime _lastSwfSyncRequestUtc = DateTime.MinValue;

        private const int GWL_STYLE = -16;
        private const int WS_CHILD = 0x40000000;
        private const int WS_BORDER = 0x00800000;
        private const int WS_DLGFRAME = 0x00400000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public Form1()
        {
            InitializeComponent();
            _settings = Settings.Load();

            UpdateWindowTitle(null);
            ApplyToolbarIcons();
        }

        private void UpdateWindowTitle(string? fileName)
        {
            Text = fileName == null ? "Legacy Console Pack Editor" : $"Legacy Console Pack Editor - {fileName}";
        }

        private void ApplyToolbarIcons()
        {
            toolStripButtonPckExtract.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            toolStripButtonPckOpen.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            toolStripButtonPckReplace.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            toolStripButtonPckDelete.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;

            toolStripButtonPckExtract.Image = SystemIcons.Information.ToBitmap();
            toolStripButtonPckOpen.Image = SystemIcons.Application.ToBitmap();
            toolStripButtonPckReplace.Image = SystemIcons.Warning.ToBitmap();
            toolStripButtonPckDelete.Image = SystemIcons.Error.ToBitmap();
        }

        private async void openArchiveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openFileDialog.Filter = "Minecraft Archive (*.arc)|*.arc|PCK File (*.pck)|*.pck|SWF File (*.swf)|*.swf|All Files (*.*)|*.*";
            openFileDialog.Multiselect = true;
            if (openFileDialog.ShowDialog() != DialogResult.OK)
                return;

            foreach (var path in openFileDialog.FileNames)
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".arc")
                    LoadArchive(path);
                else if (ext == ".pck")
                    LoadPck(path);
                else if (ext == ".swf")
                    await OpenSwfFileAsync(path, Path.GetFileName(path));
                else
                    MessageBox.Show(this, $"Unsupported file type: {path}", "Open", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Form1_DragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            if (files.Any(f => new[] { ".arc", ".pck", ".swf" }.Contains(Path.GetExtension(f).ToLowerInvariant())))
                e.Effect = DragDropEffects.Copy;
            else
                e.Effect = DragDropEffects.None;
        }

        private async void Form1_DragDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null)
                return;

            foreach (var path in files)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".arc")
                    LoadArchive(path);
                else if (ext == ".pck")
                    LoadPck(path);
                else if (ext == ".swf")
                    await OpenSwfFileAsync(path, Path.GetFileName(path));
            }
        }

        private void LoadArchive(string path)
        {
            try
            {
                _archive = _arcReader.FromFile(path);
                _archivePath = path;
                _mode = EditorMode.Arc;
                BuildArchiveTree();
                UpdateWindowTitle(Path.GetFileName(path));
                toolStripStatusLabel1.Text = $"Loaded {_archive.Count} entries from {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to open archive: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadPck(string path)
        {
            try
            {
                _pckFile = _pckReader.FromFile(path);
                _pckFilePath = path;

                _standalonePckFile = _pckFile;
                _standalonePckPath = path;
                _currentPckKey = null;

                if (_archive == null)
                    _mode = EditorMode.Pck;
                PopulatePckTree(_pckFile);
                PopulatePckAssetList(_pckFile);
                tabControlMain.SelectedTab = tabPagePck;

                if (_archive == null)
                {
                    treeViewArchive.Nodes.Clear();
                    treeViewArchive.Nodes.Add(new TreeNode($"Standalone PCK: {Path.GetFileName(path)}")
                    {
                        Tag = "__standalone_pck__"
                    });
                }
                else
                {
                    EnsureStandalonePckNode();
                }

                UpdateWindowTitle(Path.GetFileName(path));
                toolStripStatusLabel1.Text = _archive == null
                    ? $"Loaded PCK: {Path.GetFileName(path)} ({_pckFile.AssetCount} assets)"
                    : $"Loaded standalone PCK alongside ARC: {Path.GetFileName(path)} ({_pckFile.AssetCount} assets)";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to open PCK:\n{ex}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BuildArchiveTree()
        {
            treeViewArchive.BeginUpdate();
            treeViewArchive.Nodes.Clear();
            if (_archive != null)
            {
                foreach (var entry in _archive.OrderBy(kv => kv.Key))
                {
                    AddArchiveEntryNode(treeViewArchive.Nodes, entry.Key);
                }
            }

            EnsureStandalonePckNode();
            treeViewArchive.EndUpdate();
        }

        private void EnsureStandalonePckNode()
        {
            if (_standalonePckFile == null || string.IsNullOrEmpty(_standalonePckPath))
                return;

            const string standaloneNodeKey = "__standalone_pck_node__";
            var existing = treeViewArchive.Nodes.Find(standaloneNodeKey, false).FirstOrDefault();
            string nodeText = $"Standalone PCK: {Path.GetFileName(_standalonePckPath)}";
            if (existing != null)
            {
                existing.Text = nodeText;
                existing.Tag = "__standalone_pck__";
                return;
            }

            var node = new TreeNode(nodeText)
            {
                Name = standaloneNodeKey,
                Tag = "__standalone_pck__"
            };
            treeViewArchive.Nodes.Add(node);
        }

        private void ClearArchive()
        {
            _archive = null;
            _archivePath = null;
            _mode = EditorMode.None;
            treeViewArchive.Nodes.Clear();
            listViewPckAssets.Items.Clear();
            pictureBoxPckPreview.Image = null;
            labelPckInfo.Text = "No selection";
            UpdateWindowTitle(null);
            toolStripStatusLabel1.Text = "Ready";
        }

        private void AddArchiveEntryNode(TreeNodeCollection root, string key)
        {
            string normalized = key.Replace('/', '\\');
            BuildNodeTreeBySeparator(root, normalized, '\\', key);
        }

        private void BuildNodeTreeBySeparator(TreeNodeCollection root, string path, char separator, string fullKey)
        {
            if (!path.Contains(separator))
            {
                AddOrGetNode(root, path, fullKey, isFolder: false);
                return;
            }

            string segment = path.Substring(0, path.IndexOf(separator));
            string remaining = path.Substring(path.IndexOf(separator) + 1);
            var node = AddOrGetNode(root, segment, null, isFolder: true);
            BuildNodeTreeBySeparator(node.Nodes, remaining, separator, fullKey);
        }

        private TreeNode AddOrGetNode(TreeNodeCollection root, string name, string fullKey, bool isFolder)
        {
            if (root.ContainsKey(name))
                return root[name];

            var node = new TreeNode(name)
            {
                Name = name,
                Tag = fullKey,
                ImageIndex = isFolder ? 0 : 1,
                SelectedImageIndex = isFolder ? 0 : 1
            };
            root.Add(node);
            return node;
        }

        private void treeViewArchive_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if ((e.Node?.Tag as string) == "__standalone_pck__" && _standalonePckFile != null)
            {
                _pckFile = _standalonePckFile;
                _pckFilePath = _standalonePckPath;
                _currentPckKey = null;

                PopulatePckTree(_pckFile);
                PopulatePckAssetList(_pckFile);
                tabControlMain.SelectedTab = tabPagePck;
                extractToolStripMenuItem.Enabled = false;
                replaceToolStripMenuItem.Enabled = false;
                deleteToolStripMenuItem.Enabled = false;
                editSwfToolStripMenuItem.Enabled = false;
                toolStripStatusLabel1.Text = "Standalone PCK workspace selected.";
                return;
            }

            string archiveKey = e.Node?.Tag as string;
            bool hasSelection = archiveKey != null && _archive != null && _archive.ContainsKey(archiveKey);
            if (hasSelection)
            {
                DisplayArchiveEntry(archiveKey);
            }
            else
            {
                ClearPreview();
            }

            string ext = hasSelection ? Path.GetExtension(archiveKey).ToLowerInvariant() : string.Empty;
            extractToolStripMenuItem.Enabled = hasSelection;
            replaceToolStripMenuItem.Enabled = hasSelection;
            deleteToolStripMenuItem.Enabled = hasSelection;
            editSwfToolStripMenuItem.Enabled = hasSelection && ext == ".swf";
        }

        private void treeViewArchive_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (!(e.Node?.Tag is string archiveKey) || _archive == null || !_archive.ContainsKey(archiveKey))
                return;

            string ext = Path.GetExtension(archiveKey).ToLowerInvariant();
            if (ext == ".swf")
            {
                editSwfToolStripMenuItem_Click(sender, EventArgs.Empty);
            }
            else if (ext == ".pck")
            {
                DisplayArchiveEntry(archiveKey);
            }
            else
            {
                extractToolStripMenuItem_Click(sender, EventArgs.Empty);
            }
        }

        private void DisplayArchiveEntry(string archiveKey)
        {
            _pckFile = null;
            _currentPckKey = null;
            listViewPckAssets.Items.Clear();
            pictureBoxPckPreview.Image = null;
            labelPckInfo.Text = "No selection";

            var data = _archive[archiveKey].Data;
            var ext = Path.GetExtension(archiveKey)?.ToLowerInvariant();

            if (IsImageExtension(ext))
            {
                try
                {
                    using var ms = new MemoryStream(data);
                    pictureBoxPckPreview.Image = Image.FromStream(ms);
                    labelPckInfo.Text = $"{Path.GetFileName(archiveKey)} ({data.Length:N0} bytes)";
                    tabControlMain.SelectedTab = tabPagePck;
                    toolStripStatusLabel1.Text = $"Previewing image: {archiveKey}";
                }
                catch (Exception ex)
                {
                    toolStripStatusLabel1.Text = $"Cannot preview image: {ex.Message}";
                }
            }
            else if (ext == ".pck")
            {
                try
                {
                    using var ms = new MemoryStream(data);
                    _pckFile = _pckReader.FromStream(ms);
                    _currentPckKey = archiveKey;
                    PopulatePckTree(_pckFile);
                    PopulatePckAssetList(_pckFile);
                    tabControlMain.SelectedTab = tabPagePck;
                    toolStripStatusLabel1.Text = $"Loaded PCK: {archiveKey} ({_pckFile.AssetCount} assets)";
                }
                catch (Exception ex)
                {
                    toolStripStatusLabel1.Text = $"Failed to read PCK: {ex.Message}";
                }
            }
            else
            {
                labelPckInfo.Text = $"{Path.GetFileName(archiveKey)} ({data.Length:N0} bytes)";
                toolStripStatusLabel1.Text = $"Selected: {archiveKey} ({data.Length:N0} bytes)";
            }
        }

        private static bool IsImageExtension(string ext)
        {
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif";
        }

        private void PopulatePckAssetList(PckFile pck, string? folderFilter = null)
        {
            listViewPckAssets.BeginUpdate();
            listViewPckAssets.Items.Clear();

            IEnumerable<PckAsset> assets = pck.GetAssets();
            if (!string.IsNullOrEmpty(folderFilter) && folderFilter != "<root>")
            {
                string prefix = folderFilter.TrimEnd('/') + "/";
                assets = assets.Where(a => a.Filename.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var asset in assets)
            {
                var item = new ListViewItem(new[] { asset.Filename, asset.Size.ToString("N0") }) { Tag = asset };
                listViewPckAssets.Items.Add(item);
            }

            listViewPckAssets.EndUpdate();
        }

        private void PopulatePckTree(PckFile pck)
        {
            treeViewPck.BeginUpdate();
            treeViewPck.Nodes.Clear();

            var root = new TreeNode("<root>") { Name = "<root>", Tag = "<root>" };
            treeViewPck.Nodes.Add(root);

            foreach (var asset in pck.GetAssets())
            {
                string path = asset.Filename.Replace('\\', '/');
                var parts = path.Split('/');
                var current = root;

                for (int i = 0; i < parts.Length; i++)
                {
                    string part = parts[i];
                    bool isFile = (i == parts.Length - 1);
                    string nodeKey = string.Join("/", parts.Take(i + 1));

                    var next = current.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Name == part);
                    if (next == null)
                    {
                        next = new TreeNode(part) { Name = part };
                        if (isFile)
                            next.Tag = asset;
                        else
                            next.Tag = nodeKey;
                        current.Nodes.Add(next);
                    }

                    current = next;
                }
            }

            root.Expand();
            treeViewPck.EndUpdate();
        }

        private void listViewPckAssets_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_pckFile == null)
                return;

            if (listViewPckAssets.SelectedItems.Count != 1)
                return;

            var asset = listViewPckAssets.SelectedItems[0].Tag as PckAsset;
            if (asset == null)
                return;

            SelectPckAsset(asset);
        }

        private void treeViewPck_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (_pckFile == null)
                return;

            if (e.Node?.Tag is PckAsset asset)
            {
                SelectPckAsset(asset);
                return;
            }

            if (e.Node?.Tag is string folder)
            {
                PopulatePckAssetList(_pckFile, folder);
                toolStripStatusLabel1.Text = $"Showing folder: {folder}";
            }
        }

        private void treeViewPck_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (_pckFile == null)
                return;

            if (e.Node?.Tag is PckAsset asset)
            {
                SelectPckAsset(asset);
            }
        }

        private void SelectPckAsset(PckAsset asset)
        {
            foreach (ListViewItem item in listViewPckAssets.Items)
            {
                if (item.Tag is PckAsset a && a == asset)
                {
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                    break;
                }
            }

            string ext = Path.GetExtension(asset.Filename).ToLowerInvariant();
            if (IsImageExtension(ext))
            {
                try
                {
                    using var ms = new MemoryStream(asset.Data);
                    pictureBoxPckPreview.Image = Image.FromStream(ms);
                    labelPckInfo.Text = $"{asset.Filename} ({asset.Size:N0} bytes)";
                    toolStripStatusLabel1.Text = $"Previewing image: {asset.Filename}";
                }
                catch (Exception ex)
                {
                    toolStripStatusLabel1.Text = $"Cannot preview image: {ex.Message}";
                }
            }
            else
            {
                pictureBoxPckPreview.Image = null;
                labelPckInfo.Text = $"{asset.Filename} ({asset.Size:N0} bytes)";
                toolStripStatusLabel1.Text = $"Selected: {asset.Filename} ({asset.Size:N0} bytes)";
            }
        }

        private void saveArchiveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_mode == EditorMode.Arc && _archive != null)
            {
                saveFileDialog.Filter = "Minecraft Archive (*.arc)|*.arc|All Files (*.*)|*.*";
                if (saveFileDialog.ShowDialog() != DialogResult.OK)
                    return;

                try
                {
                    new ARCFileWriter(_archive).WriteToFile(saveFileDialog.FileName);
                    toolStripStatusLabel1.Text = $"Saved archive to {saveFileDialog.FileName}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to save archive: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else if (_mode == EditorMode.Pck && _pckFile != null)
            {
                saveFileDialog.Filter = "PCK File (*.pck)|*.pck|All Files (*.*)|*.*";
                if (saveFileDialog.ShowDialog() != DialogResult.OK)
                    return;

                try
                {
                    using var fs = File.Create(saveFileDialog.FileName);
                    new PckFileWriter(_pckFile, ByteOrder.BigEndian).WriteToStream(fs);
                    toolStripStatusLabel1.Text = $"Saved PCK to {saveFileDialog.FileName}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to save PCK: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e) => Close();

        private async void ReloadSwfToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_lastSwfTempFile) || !File.Exists(_lastSwfTempFile))
            {
                MessageBox.Show(this, "No SWF temp file to reload. Use Edit SWF first.", "Reload SWF", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_archive == null || string.IsNullOrEmpty(_lastSwfArchiveKey) || !_archive.ContainsKey(_lastSwfArchiveKey))
            {
                MessageBox.Show(this, "Reload is only supported for SWF files within an opened .arc.", "Reload SWF", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            await SyncSwfTempToArchiveAsync(_lastSwfTempFile, _lastSwfArchiveKey, "Reimported SWF changes");
        }

        private void ExtractSelectedPckAsset()
        {
            if (_pckFile == null)
                return;

            if (listViewPckAssets.SelectedItems.Count != 1)
                return;

            var asset = listViewPckAssets.SelectedItems[0].Tag as PckAsset;
            if (asset == null)
                return;

            saveFileDialog.FileName = Path.GetFileName(asset.Filename);
            if (saveFileDialog.ShowDialog() != DialogResult.OK)
                return;

            File.WriteAllBytes(saveFileDialog.FileName, asset.Data);
            toolStripStatusLabel1.Text = $"Extracted asset {asset.Filename} to {saveFileDialog.FileName}";
        }

        private void listViewPckAssets_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (_pckFile == null)
                return;

            if (listViewPckAssets.SelectedItems.Count != 1)
                return;

            var asset = listViewPckAssets.SelectedItems[0].Tag as PckAsset;
            if (asset == null)
                return;

            string ext = Path.GetExtension(asset.Filename).ToLowerInvariant();
            if (IsImageExtension(ext))
            {
                listViewPckAssets_SelectedIndexChanged(sender, e);
                OpenAssetInPaint(asset);
            }
            else
            {
                ExtractSelectedPckAsset();
            }
        }

        private void OpenAssetInPaint(PckAsset asset)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "LegacyConsolePackEditor", "pck");
            Directory.CreateDirectory(tempDir);
            string tempFile = Path.Combine(tempDir, Path.GetFileName(asset.Filename));
            File.WriteAllBytes(tempFile, asset.Data);

            try
            {
                string paintPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mspaint.exe");
                if (!File.Exists(paintPath))
                {
                    paintPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "mspaint.exe");
                }

                if (File.Exists(paintPath))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = paintPath,
                        Arguments = $"\"{tempFile}\"",
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                    toolStripStatusLabel1.Text = $"Opened {asset.Filename} in MS Paint (save to update).";
                    return;
                }

                var fallback = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = tempFile,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(fallback);
                toolStripStatusLabel1.Text = $"Opened {asset.Filename} in default image editor (save to update).";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to open image: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void listViewPckAssets_KeyDown(object sender, KeyEventArgs e)
        {
            if (_pckFile == null)
                return;

            if (listViewPckAssets.SelectedItems.Count != 1)
                return;

            if (e.KeyCode == Keys.Delete)
            {
                deleteToolStripMenuItem_Click(sender, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                ExtractSelectedPckAsset();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.R)
            {
                replaceToolStripMenuItem_Click(sender, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.O)
            {
                OpenSelectedPckAssetInDefaultApp();
                e.Handled = true;
            }
        }

        private void toolStripButtonPckExtract_Click(object sender, EventArgs e)
        {
            ExtractSelectedPckAsset();
        }

        private void toolStripButtonPckReplace_Click(object sender, EventArgs e)
        {
            replaceToolStripMenuItem_Click(sender, e);
        }

        private void toolStripButtonPckOpen_Click(object sender, EventArgs e)
        {
            OpenSelectedPckAssetInDefaultApp();
        }

        private void toolStripButtonPckDelete_Click(object sender, EventArgs e)
        {
            deleteToolStripMenuItem_Click(sender, e);
        }

        private void OpenSelectedPckAssetInDefaultApp()
        {
            if (_pckFile == null)
                return;

            if (listViewPckAssets.SelectedItems.Count != 1)
                return;

            var asset = listViewPckAssets.SelectedItems[0].Tag as PckAsset;
            if (asset == null)
                return;

            if (!IsImageExtension(Path.GetExtension(asset.Filename).ToLowerInvariant()))
            {
                ExtractSelectedPckAsset();
                return;
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "LegacyConsolePackEditor", "pck");
            Directory.CreateDirectory(tempDir);
            string tempFile = Path.Combine(tempDir, Path.GetFileName(asset.Filename));
            File.WriteAllBytes(tempFile, asset.Data);

            SetupPckTempWatcher(tempFile, asset);

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = tempFile,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                toolStripStatusLabel1.Text = $"Opened {asset.Filename} in default app (edit and save to update).";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to open asset: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetupPckTempWatcher(string tempFile, PckAsset asset)
        {
            if (_pckTempWatcher != null)
            {
                _pckTempWatcher.Dispose();
                _pckTempWatcher = null;
            }

            _pckTempFile = tempFile;

            _pckTempWatcher = new FileSystemWatcher(Path.GetDirectoryName(tempFile) ?? string.Empty)
            {
                Filter = Path.GetFileName(tempFile),
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };

            _pckTempWatcher.Changed += (s, e) =>
            {
                try
                {
                    System.Threading.Thread.Sleep(200);
                    var bytes = File.ReadAllBytes(tempFile);

                    asset.SetData(bytes);
                    if (listViewPckAssets.SelectedItems.Count == 1 && listViewPckAssets.SelectedItems[0].Tag == asset)
                        SelectPckAsset(asset);

                    UpdatePckBytes();
                    toolStripStatusLabel1.Text = $"Reloaded edited asset: {asset.Filename}";
                }
                catch { }
            };

            _pckTempWatcher.EnableRaisingEvents = true;
        }

        private void embedSwfEditorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _embedSwfEditor = embedSwfEditorToolStripMenuItem.Checked;
            toolStripStatusLabel1.Text = _embedSwfEditor ? "Embedded SWF editor enabled." : "Embedded SWF editor disabled.";
        }

        private void extractToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_pckFile != null && listViewPckAssets.SelectedItems.Count == 1)
            {
                var asset = listViewPckAssets.SelectedItems[0].Tag as PckAsset;
                if (asset == null)
                    return;

                saveFileDialog.FileName = Path.GetFileName(asset.Filename);
                if (saveFileDialog.ShowDialog() != DialogResult.OK)
                    return;

                File.WriteAllBytes(saveFileDialog.FileName, asset.Data);
                toolStripStatusLabel1.Text = $"Extracted asset {asset.Filename} to {saveFileDialog.FileName}";
                return;
            }

            if (_archive == null)
                return;

            if (treeViewArchive.SelectedNode?.Tag is string archiveKey && _archive.ContainsKey(archiveKey))
            {
                saveFileDialog.FileName = Path.GetFileName(archiveKey);
                if (saveFileDialog.ShowDialog() != DialogResult.OK)
                    return;

                File.WriteAllBytes(saveFileDialog.FileName, _archive[archiveKey].Data);
                toolStripStatusLabel1.Text = $"Extracted {archiveKey} to {saveFileDialog.FileName}";
            }
        }

        private async void editSwfToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_archive == null)
                return;

            if (!(treeViewArchive.SelectedNode?.Tag is string archiveKey) || !_archive.ContainsKey(archiveKey))
                return;

            string ext = Path.GetExtension(archiveKey)?.ToLowerInvariant();
            if (ext != ".swf")
            {
                MessageBox.Show(this, "Select a .swf file in the archive to edit.", "Edit SWF", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "LegacyConsolePackEditor", "swf");
            Directory.CreateDirectory(tempDir);
            string tempFile = Path.Combine(tempDir, Path.GetFileName(archiveKey));
            File.WriteAllBytes(tempFile, _archive[archiveKey].Data);
            _lastSwfTempFile = tempFile;
            _lastSwfArchiveKey = archiveKey;

            SetupSwfTempWatcher(tempFile, archiveKey);

            await OpenSwfFileAsync(tempFile, archiveKey);
        }

        private void SetupSwfTempWatcher(string tempFile, string archiveKey)
        {
            if (_swfTempWatcher != null)
            {
                _swfTempWatcher.Dispose();
                _swfTempWatcher = null;
            }

            _swfTempWatcher = new FileSystemWatcher(Path.GetDirectoryName(tempFile) ?? string.Empty)
            {
                Filter = Path.GetFileName(tempFile),
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };

            void queueSwfSyncFromTemp()
            {
                var now = DateTime.UtcNow;
                if ((now - _lastSwfSyncRequestUtc).TotalMilliseconds < 250)
                    return;

                _lastSwfSyncRequestUtc = now;
                BeginInvoke((Action)(async () =>
                {
                    await SyncSwfTempToArchiveAsync(tempFile, archiveKey, "Auto-synced SWF save");
                }));
            }

            _swfTempWatcher.Changed += (s, e) => queueSwfSyncFromTemp();
            _swfTempWatcher.Created += (s, e) => queueSwfSyncFromTemp();
            _swfTempWatcher.Renamed += (s, e) => queueSwfSyncFromTemp();

            _swfTempWatcher.EnableRaisingEvents = true;
        }

        private static byte[]? TryReadFileBytesWithRetry(string filePath)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    return File.ReadAllBytes(filePath);
                }
                catch
                {
                    System.Threading.Thread.Sleep(120);
                }
            }

            return null;
        }

        private async Task SyncSwfTempToArchiveAsync(string tempFile, string archiveKey, string statusPrefix)
        {
            if (_archive == null || !_archive.ContainsKey(archiveKey))
                return;

            if (!File.Exists(tempFile))
                return;

            var bytes = TryReadFileBytesWithRetry(tempFile);
            if (bytes == null)
                return;

            _archive[archiveKey] = new ConsoleArchiveEntry(bytes);

            if (webView2Swf.Visible && tabControlMain.SelectedTab == tabPageSwfEditor)
            {
                await DisplaySwfInInternalViewerAsync(tempFile);
            }

            toolStripStatusLabel1.Text = $"{statusPrefix} into archive: {archiveKey}";
        }

        private void OpenSwfExternally(string jar, string tempFile, string archiveKey)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "java",
                    Arguments = $"-jar \"{jar}\" \"{tempFile}\"",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                toolStripStatusLabel1.Text = $"Opened SWF externally: {archiveKey}.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to launch SWF editor: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task OpenSwfFileAsync(string swfPath, string displayName)
        {
            if (_embedSwfEditor)
            {
                bool loadedInViewer = await DisplaySwfInInternalViewerAsync(swfPath);
                if (loadedInViewer)
                    return;

                var fallbackJar = GetSwfEditorJarPath();
                if (fallbackJar != null)
                {
                    TryEmbedSwfEditor(fallbackJar, swfPath, displayName);
                    return;
                }
            }

            var jar = GetSwfEditorJarPath();
            if (jar == null)
            {
                var result = MessageBox.Show(this, "Could not locate ffdec.jar. Would you like to locate it manually?", "SWF Editor Not Found", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result == DialogResult.Yes)
                {
                    using var jarDialog = new OpenFileDialog
                    {
                        Filter = "Java JAR File (*.jar)|*.jar",
                        Title = "Select ffdec.jar"
                    };
                    if (jarDialog.ShowDialog(this) == DialogResult.OK)
                    {
                        _settings.SwfEditorJarPath = jarDialog.FileName;
                        _settings.Save();
                        jar = jarDialog.FileName;
                    }
                }

                if (jar == null)
                    return;
            }

            OpenSwfExternally(jar, swfPath, displayName);
        }

        private async Task<bool> DisplaySwfInInternalViewerAsync(string swfFilePath)
        {
            try
            {
                byte[] swfBytes = File.ReadAllBytes(swfFilePath);
                string base64 = Convert.ToBase64String(swfBytes);

                var runtimeCandidates = new List<string>
                {
                    Path.Combine(AppContext.BaseDirectory, "ruffle", "ruffle.js"),
                    Path.Combine(Application.StartupPath, "ruffle", "ruffle.js"),
                    Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? AppContext.BaseDirectory, "ruffle", "ruffle.js")
                };

                string? runtimeScript = null;
                foreach (var candidate in runtimeCandidates.Where(File.Exists).Distinct())
                {
                    string candidateScript = File.ReadAllText(candidate);
                    if (candidateScript.IndexOf("RufflePlayer", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        candidateScript.IndexOf("Placeholder", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        runtimeScript = candidateScript;
                        break;
                    }
                }

                string ruffleScriptTag;
                if (runtimeScript != null)
                {
                    ruffleScriptTag = $"<script>{runtimeScript}</script>";
                    toolStripStatusLabel1.Text = $"Viewing SWF (offline runtime): {Path.GetFileName(swfFilePath)}";
                }
                else
                {
                    toolStripStatusLabel1.Text = "Built-in SWF runtime missing, falling back to embedded FFDec.";
                    return false;
                }

                string html = "<!DOCTYPE html>" +
                    "<html><head><meta charset=\"utf-8\"/>" +
                    "<style>body{margin:0;background:#000;color:#eee;display:flex;justify-content:center;align-items:center;font-family:Segoe UI,Arial,sans-serif;}</style>" +
                    ruffleScriptTag +
                    "</head><body><div id=\"player\" style=\"width:100%;height:100%;\"></div>" +
                    "<script>function initRuffle(){if(!window.RufflePlayer){document.body.innerHTML='<div style=\"padding:16px;text-align:center;\">Ruffle runtime failed to load.<br/>Put a real ruffle.js into app/ruffle or keep internet on for CDN fallback.</div>';return;}const ruffle=window.RufflePlayer.newest();const player=ruffle.createPlayer();document.getElementById('player').appendChild(player);player.style.width='100%';player.style.height='100%';player.load('data:application/x-shockwave-flash;base64," + base64 + "');}window.addEventListener('DOMContentLoaded',initRuffle);</script>" +
                    "</body></html>";

                panelSwfHost.Visible = false;
                webView2Swf.Visible = true;

                await webView2Swf.EnsureCoreWebView2Async();
                webView2Swf.NavigateToString(html);

                tabControlMain.SelectedTab = tabPageSwfEditor;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to display SWF: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void TryEmbedSwfEditor(string jar, string tempFile, string archiveKey)
        {
            try
            {
                webView2Swf.Visible = false;
                panelSwfHost.Visible = true;

                CleanupEmbeddedSwfEditor();
                StartSwfEmbedMonitor();
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "java",
                    Arguments = $"-jar \"{jar}\" \"{tempFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                _swfEditorProcess = System.Diagnostics.Process.Start(psi);
                if (_swfEditorProcess == null)
                    throw new InvalidOperationException("Failed to start SWF editor process.");

                var swfHandle = IntPtr.Zero;
                for (int i = 0; i < 150; i++)
                {
                    _swfEditorProcess.Refresh();
                    swfHandle = _swfEditorProcess.MainWindowHandle;
                    if (swfHandle != IntPtr.Zero)
                        break;

                    System.Threading.Thread.Sleep(100);
                }

                if (swfHandle == IntPtr.Zero)
                {
                        swfHandle = FindWindowHandleForProcess(_swfEditorProcess.Id);
                }

                if (swfHandle == IntPtr.Zero)
                {
                    toolStripStatusLabel1.Text = $"Launched SWF editor (external window).";
                    return;
                }

                _swfEditorHandle = swfHandle;
                SetParent(_swfEditorHandle, panelSwfHost.Handle);

                int style = GetWindowLong(_swfEditorHandle, GWL_STYLE);
                style = (style | WS_CHILD) & ~(WS_BORDER | WS_DLGFRAME);
                SetWindowLong(_swfEditorHandle, GWL_STYLE, style);

                ResizeEmbeddedEditor();
                if (!_swfResizeHooked)
                {
                    panelSwfHost.Resize += (s, e) => ResizeEmbeddedEditor();
                    _swfResizeHooked = true;
                }

                tabControlMain.SelectedTab = tabPageSwfEditor;
                toolStripStatusLabel1.Text = $"Embedded SWF editor for {archiveKey}.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to launch embedded SWF editor: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StartSwfEmbedMonitor()
        {
            if (_swfEmbedMonitorTimer != null)
                return;

            _swfEmbedMonitorTimer = new System.Windows.Forms.Timer();
            _swfEmbedMonitorTimer.Interval = 750;
            _swfEmbedMonitorTimer.Tick += (s, e) =>
            {
                if (_swfEditorProcess == null || _swfEditorProcess.HasExited)
                {
                    _swfEmbedMonitorTimer.Stop();
                    return;
                }

                var handle = _swfEditorProcess.MainWindowHandle;
                if (handle != IntPtr.Zero && handle != _swfEditorHandle)
                {
                    _swfEditorHandle = handle;
                    SetParent(_swfEditorHandle, panelSwfHost.Handle);
                    int style = GetWindowLong(_swfEditorHandle, GWL_STYLE);
                    style = (style | WS_CHILD) & ~(WS_BORDER | WS_DLGFRAME);
                    SetWindowLong(_swfEditorHandle, GWL_STYLE, style);
                    ResizeEmbeddedEditor();
                }
            };
            _swfEmbedMonitorTimer.Start();
        }

        private void StopSwfEmbedMonitor()
        {
            if (_swfEmbedMonitorTimer == null)
                return;

            _swfEmbedMonitorTimer.Stop();
            _swfEmbedMonitorTimer.Dispose();
            _swfEmbedMonitorTimer = null;
        }

        private void ResizeEmbeddedEditor()
        {
            if (_swfEditorHandle == IntPtr.Zero || panelSwfHost.IsDisposed)
                return;

            MoveWindow(_swfEditorHandle, 0, 0, panelSwfHost.ClientSize.Width, panelSwfHost.ClientSize.Height, true);
        }

        private void CleanupEmbeddedSwfEditor()
        {
            try
            {
                StopSwfEmbedMonitor();

                if (_swfEditorProcess != null && !_swfEditorProcess.HasExited)
                {
                    _swfEditorProcess.Kill(true);
                    _swfEditorProcess.Dispose();
                }
            }
            catch { }
            finally
            {
                _swfEditorProcess = null;
                _swfEditorHandle = IntPtr.Zero;
            }
        }

        private IntPtr FindWindowHandleForProcess(int processId)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd))
                    return true;

                _ = GetWindowThreadProcessId(hWnd, out uint windowPid);
                if (windowPid != processId)
                    return true;

                var sb = new StringBuilder(256);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();

                if (title.IndexOf("JPEXS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    title.IndexOf("ffdec", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    title.IndexOf("Flash", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = hWnd;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
            return found;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            CleanupEmbeddedSwfEditor();

            if (_swfTempWatcher != null)
            {
                _swfTempWatcher.Dispose();
                _swfTempWatcher = null;
            }
        }

        private string? GetSwfEditorJarPath()
        {
            if (!string.IsNullOrEmpty(_settings.SwfEditorJarPath) && File.Exists(_settings.SwfEditorJarPath))
                return _settings.SwfEditorJarPath;

            string baseDir = AppContext.BaseDirectory;
            string candidate = Path.Combine(baseDir, "ffdec", "ffdec.jar");
            if (File.Exists(candidate))
                return candidate;

            candidate = Path.Combine(baseDir, "ffdec.jar");
            if (File.Exists(candidate))
                return candidate;

            string alt = Path.Combine(baseDir, "..", "ffdec", "ffdec.jar");
            if (File.Exists(alt))
                return Path.GetFullPath(alt);

            return null;
        }

        private void replaceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_pckFile != null && listViewPckAssets.SelectedItems.Count == 1)
            {
                var asset = listViewPckAssets.SelectedItems[0].Tag as PckAsset;
                if (asset != null)
                {
                    string assetExt = Path.GetExtension(asset.Filename).ToLowerInvariant();
                    if (IsImageExtension(assetExt))
                        openFileDialog.Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All Files (*.*)|*.*";
                    else
                        openFileDialog.Filter = "All Files (*.*)|*.*";

                    if (openFileDialog.ShowDialog() != DialogResult.OK)
                        return;

                    var replacementData = File.ReadAllBytes(openFileDialog.FileName);
                    asset.SetData(replacementData);
                    PersistPckChanges();
                    listViewPckAssets.SelectedItems[0].SubItems[1].Text = asset.Size.ToString("N0");
                    SelectPckAsset(asset);
                    toolStripStatusLabel1.Text = $"Replaced asset {asset.Filename}";
                }
                return;
            }

            if (_archive == null)
                return;

            openFileDialog.Filter = "All Files (*.*)|*.*";
            if (openFileDialog.ShowDialog() != DialogResult.OK)
                return;

            var archiveReplacementData = File.ReadAllBytes(openFileDialog.FileName);

            if (treeViewArchive.SelectedNode?.Tag is string archiveKey && _archive.ContainsKey(archiveKey))
            {
                _archive[archiveKey] = new ConsoleArchiveEntry(archiveReplacementData);
                toolStripStatusLabel1.Text = $"Replaced file {archiveKey}";
            }
        }

        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_pckFile != null && listViewPckAssets.SelectedItems.Count == 1)
            {
                var asset = listViewPckAssets.SelectedItems[0].Tag as PckAsset;
                if (asset != null)
                {
                    _pckFile.RemoveAsset(asset);
                    PersistPckChanges();
                    listViewPckAssets.Items.Remove(listViewPckAssets.SelectedItems[0]);
                    toolStripStatusLabel1.Text = $"Removed asset {asset.Filename}";
                }
                return;
            }

            if (_archive == null)
                return;

            if (treeViewArchive.SelectedNode?.Tag is string archiveKey && _archive.ContainsKey(archiveKey))
            {
                _archive.Remove(archiveKey);
                treeViewArchive.SelectedNode.Remove();
                toolStripStatusLabel1.Text = $"Removed {archiveKey} from archive";
            }
        }

        private void PersistPckChanges()
        {
            if (_pckFile == null)
                return;

            if (!string.IsNullOrEmpty(_currentPckKey) && _archive != null)
            {
                UpdatePckBytes();
                return;
            }

            if (!string.IsNullOrEmpty(_pckFilePath))
            {
                using var fs = File.Create(_pckFilePath);
                new PckFileWriter(_pckFile, ByteOrder.BigEndian).WriteToStream(fs);
            }
        }

        private void UpdatePckBytes()
        {
            if (string.IsNullOrEmpty(_currentPckKey) || _archive == null || _pckFile == null)
                return;

            using var ms = new MemoryStream();
            new PckFileWriter(_pckFile, ByteOrder.BigEndian).WriteToStream(ms);
            _archive[_currentPckKey] = new ConsoleArchiveEntry(ms.ToArray());
        }

        private void ClearPreview()
        {
            pictureBoxPckPreview.Image = null;
            labelPckInfo.Text = "No selection";
            listViewPckAssets.Items.Clear();
            toolStripStatusLabel1.Text = "Ready";
        }
    }
}
