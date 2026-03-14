using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using OMI;
using OMI.Formats.Archive;
using OMI.Formats.Pck;
using OMI.Workers.Archive;
using OMI.Workers.Pck;
using System.Windows.Forms;

namespace LegacyConsolePackEditor
{
    public partial class Form1 : Form
    {
        private enum EditorMode
        {
            None,
            Arc,
            Pck
        }

        private enum ImageEditorTool
        {
            Pencil,
            Brush,
            Eyedropper,
            Eraser,
            MovePastedLayer,
            Hand
        }

        private const int GWL_STYLE = -16;
        private const int WS_CHILD = 0x40000000;
        private const int WS_BORDER = 0x00800000;
        private const int WS_DLGFRAME = 0x00400000;

        private static readonly Color SurfaceColor = Color.FromArgb(247, 247, 244);
        private static readonly Color SurfaceAltColor = Color.FromArgb(232, 233, 227);
        private static readonly Color ForegroundColor = Color.FromArgb(34, 38, 44);
        private static readonly Color BorderColor = Color.FromArgb(182, 188, 198);
        private static readonly Color AccentColor = Color.FromArgb(52, 120, 96);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);

        private readonly ARCFileReader _arcReader = new();
        private readonly PckFileReader _pckReader = new(ByteOrder.BigEndian);
        private readonly Settings _settings;

        private const string WorkspacePcksRootTag = "__workspace_pcks_root__";
        private const string WorkspacePckTagPrefix = "__workspace_pck__|";

        private ConsoleArchive? _archive;
        private string? _archivePath;
        private PckFile? _pckFile;
        private string? _pckFilePath;
        private readonly Dictionary<string, WorkspacePckContext> _workspacePcks = new(StringComparer.OrdinalIgnoreCase);
        private string? _workspaceFolderPath;
        private string? _currentPckKey;
        private EditorMode _mode = EditorMode.None;

        private string? _lastSwfArchiveKey;
        private string? _lastSwfTempFile;
        private DateTime _lastSwfSyncRequestUtc;
        private FileSystemWatcher? _pckTempWatcher;
        private FileSystemWatcher? _swfTempWatcher;
        private Process? _swfEditorProcess;
        private IntPtr _swfEditorHandle = IntPtr.Zero;
        private System.Windows.Forms.Timer? _swfEmbedMonitorTimer;
        private bool _embedSwfEditor;

        private sealed class WorkspacePckContext
        {
            public required string Path { get; init; }
            public required PckFile File { get; init; }
        }

        public Form1()
        {
            _settings = Settings.Load();

            InitializeComponent();
            ConfigureListAndTreeViews();
            _embedSwfEditor = embedSwfEditorToolStripMenuItem.Checked;
            UpdateWindowTitle(null);

            // Keep embedded editor host in sync when the tab is resized.
            panelSwfHost.Resize += (s, e) => ResizeEmbeddedEditor();
        }

        private void ConfigureListAndTreeViews()
        {
            listViewPckAssets.View = View.Details;
            listViewPckAssets.FullRowSelect = true;
            listViewPckAssets.GridLines = true;
            listViewPckAssets.MultiSelect = false;

            if (listViewPckAssets.Columns.Count == 0)
            {
                listViewPckAssets.Columns.Add("Filename", 420);
                listViewPckAssets.Columns.Add("Size", 120, HorizontalAlignment.Right);
                listViewPckAssets.Columns.Add("Type", 120);
            }
        }

        private void UpdateWindowTitle(string? fileName)
        {
            string suffix = "Legacy Console Pack Editor";
            Text = string.IsNullOrWhiteSpace(fileName) ? suffix : $"{fileName} - {suffix}";
        }

        private static Font CreateFriendlyFont(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }

        private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        private static bool WriteUtf8IntoFixedSegment(byte[] buffer, int offset, int capacity, string value)
        {
            if (offset < 0 || capacity < 0 || offset >= buffer.Length)
                return false;

            int maxWritable = Math.Min(capacity, buffer.Length - offset);
            if (maxWritable <= 0)
                return false;

            Array.Fill(buffer, (byte)0, offset, maxWritable);
            byte[] encoded = Encoding.UTF8.GetBytes(value ?? string.Empty);
            int copyLength = Math.Min(encoded.Length, maxWritable);
            Buffer.BlockCopy(encoded, 0, buffer, offset, copyLength);
            return encoded.Length > maxWritable;
        }

        private static bool WriteUtf8IntoSegment(byte[] buffer, EditableTextSegment segment, string value)
        {
            string fullValue = (segment.HiddenPrefix ?? string.Empty) + (value ?? string.Empty);

            if (!segment.HasLengthPrefix)
                return WriteUtf8IntoFixedSegment(buffer, segment.TextOffset, segment.TextCapacity, fullValue);

            if (segment.Offset < 0 || segment.Offset + 1 >= buffer.Length)
                return false;

            int maxWritable = Math.Min(segment.TextCapacity, buffer.Length - segment.TextOffset);
            if (maxWritable <= 0)
                return false;

            Array.Fill(buffer, (byte)0, segment.TextOffset, maxWritable);
            byte[] encoded = Encoding.UTF8.GetBytes(fullValue);

            int copyLength = Math.Min(encoded.Length, Math.Min(maxWritable, 65535));
            Buffer.BlockCopy(encoded, 0, buffer, segment.TextOffset, copyLength);

            // LOC strings use a 2-byte big-endian length prefix.
            buffer[segment.Offset] = (byte)((copyLength >> 8) & 0xFF);
            buffer[segment.Offset + 1] = (byte)(copyLength & 0xFF);

            return encoded.Length > maxWritable || encoded.Length > 65535;
        }

        private static List<EditableTextSegment> ExtractEditableTextSegments(byte[] bytes)
        {
            var rawSegments = ExtractRawLocSegments(bytes);
            var rows = new List<EditableTextSegment>();
            string currentLanguage = string.Empty;
            RawLocSegment? pendingKey = null;

            foreach (var raw in rawSegments)
            {
                if (LooksLikeLanguageCode(raw.Decoded))
                {
                    currentLanguage = raw.Decoded;
                    pendingKey = null;
                    continue;
                }

                if (LooksLikeLocKey(raw.Decoded))
                {
                    pendingKey = raw;
                    continue;
                }

                string display = CleanDisplayText(StripHiddenDisplayPrefix(raw.Decoded, out string directHiddenPrefix));
                if (pendingKey != null && IsLikelyEditableText(display))
                {
                    rows.Add(new EditableTextSegment
                    {
                        Offset = raw.Offset,
                        Capacity = raw.TextCapacity + 2,
                        TextOffset = raw.TextOffset + Encoding.UTF8.GetByteCount(directHiddenPrefix),
                        TextCapacity = Math.Max(0, raw.TextCapacity - Encoding.UTF8.GetByteCount(directHiddenPrefix)),
                        TextLength = Math.Max(0, raw.TextLength - Encoding.UTF8.GetByteCount(directHiddenPrefix)),
                        HasLengthPrefix = true,
                        HiddenPrefix = directHiddenPrefix,
                        Label = $"{currentLanguage} / {pendingKey.Decoded}",
                        Text = display
                    });
                    pendingKey = null;
                    continue;
                }

                pendingKey = null;
                var expanded = ExpandCompositeLocSegment(raw, currentLanguage);
                if (expanded.Count > 0)
                {
                    rows.AddRange(expanded);
                    continue;
                }

                if (IsLikelyEditableText(display))
                {
                    rows.Add(new EditableTextSegment
                    {
                        Offset = raw.Offset,
                        Capacity = raw.TextCapacity + 2,
                        TextOffset = raw.TextOffset + Encoding.UTF8.GetByteCount(directHiddenPrefix),
                        TextCapacity = Math.Max(0, raw.TextCapacity - Encoding.UTF8.GetByteCount(directHiddenPrefix)),
                        TextLength = Math.Max(0, raw.TextLength - Encoding.UTF8.GetByteCount(directHiddenPrefix)),
                        HasLengthPrefix = true,
                        HiddenPrefix = directHiddenPrefix,
                        Label = string.IsNullOrEmpty(currentLanguage) ? "Text" : currentLanguage,
                        Text = display
                    });
                }
            }

            return rows;
        }

        private static List<RawLocSegment> ExtractRawLocSegments(byte[] bytes)
        {
            var segments = new Dictionary<int, RawLocSegment>();

            for (int i = 0; i + 2 <= bytes.Length; i++)
            {
                int length = (bytes[i] << 8) | bytes[i + 1];
                if (length <= 0)
                    continue;

                int textOffset = i + 2;
                if (textOffset + length > bytes.Length)
                    continue;

                if (!TryDecodeUtf8(bytes, textOffset, length, out string decoded))
                    continue;

                string displayText = CleanDisplayText(StripHiddenDisplayPrefix(decoded));
                if (!LooksLikeLanguageCode(decoded) && !LooksLikeLocKey(decoded) && !IsLikelyEditableText(displayText) && !decoded.Contains("IDS_", StringComparison.Ordinal))
                    continue;

                int next = textOffset + length;
                while (next < bytes.Length && bytes[next] == 0)
                    next++;

                int nextPrefix = next + 1 < bytes.Length ? ((bytes[next] << 8) | bytes[next + 1]) : 0;
                bool hasAnother = next + 2 <= bytes.Length && nextPrefix > 0 && next + 2 + nextPrefix <= bytes.Length;

                int textCapacity = hasAnother
                    ? Math.Max(length, next - textOffset)
                    : length;

                segments[i] = new RawLocSegment
                {
                    Offset = i,
                    TextOffset = textOffset,
                    TextCapacity = textCapacity,
                    TextLength = length,
                    Decoded = decoded
                };
            }

            return segments.Values.OrderBy(s => s.Offset).ToList();
        }

        private static List<EditableTextSegment> ExpandCompositeLocSegment(RawLocSegment raw, string currentLanguage)
        {
            var rows = new List<EditableTextSegment>();
            string decoded = raw.Decoded;
            const string pattern = "(?:(?<lang>[a-z]{2}-[A-Z]{2,3}))?(?<key>IDS_(?:TP_DESCRIPTION|DISPLAY_NAME))(?<payload>.*?)(?=(?:(?:[a-z]{2}-[A-Z]{2,3})?IDS_(?:TP_DESCRIPTION|DISPLAY_NAME))|$)";
            var matches = Regex.Matches(decoded, pattern);
            if (matches.Count == 0)
                return rows;

            string language = currentLanguage;
            foreach (Match match in matches)
            {
                if (match.Length == 0)
                    continue;

                string matchedLanguage = match.Groups["lang"].Value;
                if (LooksLikeLanguageCode(matchedLanguage))
                    language = matchedLanguage;

                string key = match.Groups["key"].Value;
                string payload = match.Groups["payload"].Value;
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(payload))
                    continue;

                string hiddenPrefix = string.Empty;
                string rawValue = payload;
                if (key.Equals("IDS_TP_DESCRIPTION", StringComparison.Ordinal) && rawValue.Length > 0)
                {
                    hiddenPrefix = rawValue.Substring(0, 1);
                    rawValue = rawValue.Substring(1);
                }

                string display = CleanDisplayText(rawValue);
                if (!IsLikelyEditableText(display))
                    continue;

                int payloadStartChar = match.Index + match.Length - payload.Length;
                int prefixBytes = Encoding.UTF8.GetByteCount(decoded.Substring(0, payloadStartChar));
                int hiddenBytes = Encoding.UTF8.GetByteCount(hiddenPrefix);
                int totalValueBytes = Encoding.UTF8.GetByteCount(payload);
                bool reachesEnd = payloadStartChar + payload.Length >= decoded.Length;
                int trailingCapacity = reachesEnd ? Math.Max(0, raw.TextCapacity - raw.TextLength) : 0;

                rows.Add(new EditableTextSegment
                {
                    Offset = raw.TextOffset + prefixBytes,
                    Capacity = totalValueBytes + trailingCapacity,
                    TextOffset = raw.TextOffset + prefixBytes + hiddenBytes,
                    TextCapacity = Math.Max(0, totalValueBytes - hiddenBytes + trailingCapacity),
                    TextLength = Math.Max(0, totalValueBytes - hiddenBytes),
                    HasLengthPrefix = false,
                    HiddenPrefix = hiddenPrefix,
                    Label = string.IsNullOrEmpty(language) ? key : $"{language} / {key}",
                    Text = display
                });
            }

            return rows;
        }

        private static bool LooksLikeLanguageCode(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, "^[a-z]{2}-[A-Z]{2,3}$");
        }

        private static bool LooksLikeLocKey(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.StartsWith("IDS_", StringComparison.Ordinal);
        }

        private static bool TryDecodeUtf8(byte[] bytes, int offset, int count, out string decoded)
        {
            decoded = string.Empty;
            try
            {
                decoded = StrictUtf8.GetString(bytes, offset, count);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool LooksLikeLocControlString(string value)
        {
            return LooksLikeLanguageCode(value) || LooksLikeLocKey(value);
        }

        private static string StripHiddenDisplayPrefix(string value, out string hiddenPrefix)
        {
            if (string.IsNullOrEmpty(value))
            {
                hiddenPrefix = string.Empty;
                return string.Empty;
            }

            int index = 0;
            while (index < value.Length && (value[index] == '\u001b' || value[index] == '='))
                index++;

            hiddenPrefix = index == 0 ? string.Empty : value.Substring(0, index);
            return value.Substring(index);
        }

        private static string StripHiddenDisplayPrefix(string value)
        {
            return StripHiddenDisplayPrefix(value, out _);
        }

        private static string CleanDisplayText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var filtered = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t')
                    continue;

                filtered.Append(c);
            }

            return filtered.ToString();
        }

        private static string EncodeGridText(string value)
        {
            return value.Replace("\r\n", "\\n").Replace("\n", "\\n");
        }

        private static string DecodeGridText(string value)
        {
            return value.Replace("\\r\\n", "\n").Replace("\\n", "\n");
        }

        private static bool IsLikelyEditableText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            int printable = 0;
            int valid = 0;
            foreach (char c in value)
            {
                if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t')
                    continue;

                valid++;
                if (!char.IsControl(c))
                    printable++;
            }

            if (valid == 0)
                return false;

            bool hasLetterOrDigit = value.Any(char.IsLetterOrDigit);
            double ratio = printable / (double)valid;
            return hasLetterOrDigit && ratio >= 0.75;
        }

        private static string BuildAsciiPreview(byte[] bytes)
        {
            if (bytes.Length == 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (int offset = 0; offset < bytes.Length; offset += 16)
            {
                int lineLen = Math.Min(16, bytes.Length - offset);
                sb.Append(offset.ToString("X8"));
                sb.Append("  ");

                for (int i = 0; i < 16; i++)
                {
                    if (i < lineLen)
                        sb.Append(bytes[offset + i].ToString("X2"));
                    else
                        sb.Append("  ");

                    if (i < 15)
                        sb.Append(' ');
                }

                sb.Append("  | ");
                for (int i = 0; i < lineLen; i++)
                {
                    byte b = bytes[offset + i];
                    char c = (b >= 32 && b <= 126) ? (char)b : '.';
                    sb.Append(c);
                }

                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("Detected LOC rows:");
            var segments = ExtractEditableTextSegments(bytes);
            if (segments.Count == 0)
            {
                sb.AppendLine("(none)");
            }
            else
            {
                foreach (var seg in segments)
                {
                    string text = CleanDisplayText(seg.Text).Replace("\r\n", "\\n").Replace("\n", "\\n");
                    sb.AppendLine($"0x{seg.Offset:X6} len={seg.TextLength} cap={seg.TextCapacity} text={text}");
                }
            }

            return sb.ToString();
        }

        private async void openArchiveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openFileDialog.Filter = "Console Files (*.arc;*.pck;*.swf)|*.arc;*.pck;*.swf|ARC Files (*.arc)|*.arc|PCK Files (*.pck)|*.pck|SWF Files (*.swf)|*.swf|All Files (*.*)|*.*";
            if (openFileDialog.ShowDialog() != DialogResult.OK)
                return;

            foreach (var path in openFileDialog.FileNames)
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
                else
                    MessageBox.Show(this, $"Unsupported file type: {path}", "Open", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void openFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(folderBrowserDialog.SelectedPath))
                return;

            LoadWorkspaceFolder(folderBrowserDialog.SelectedPath);
        }

        private void LoadWorkspaceFolder(string folderPath)
        {
            try
            {
                string fullFolder = Path.GetFullPath(folderPath);
                _workspaceFolderPath = fullFolder;
                _workspacePcks.Clear();
                _currentPckKey = null;
                _pckFile = null;
                _pckFilePath = null;

                string? mediaArcPath = FindPreferredWorkspaceArc(fullFolder);
                if (!string.IsNullOrEmpty(mediaArcPath))
                {
                    _archive = _arcReader.FromFile(mediaArcPath);
                    _archivePath = mediaArcPath;
                    _mode = EditorMode.Arc;
                }
                else
                {
                    _archive = null;
                    _archivePath = null;
                    _mode = EditorMode.None;
                }

                foreach (string pckPath in FindWorkspacePckFiles(fullFolder))
                {
                    try
                    {
                        PckFile file = _pckReader.FromFile(pckPath);
                        _workspacePcks[pckPath] = new WorkspacePckContext { Path = pckPath, File = file };
                    }
                    catch
                    {
                        // Skip unreadable pack files but keep loading the rest of the workspace.
                    }
                }

                if (_archive == null && _workspacePcks.Count == 0)
                {
                    MessageBox.Show(this, "No supported Media.arc or .pck files were found in that folder.", "Open Folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    ClearArchive();
                    return;
                }

                if (_archive == null && _workspacePcks.Count > 0)
                    _mode = EditorMode.Pck;

                BuildArchiveTree();

                if (_workspacePcks.Count > 0)
                {
                    SelectWorkspacePck(_workspacePcks.Values.First().Path);
                    tabControlMain.SelectedTab = tabPagePck;
                }
                else if (_archive != null)
                {
                    ClearPreview();
                }

                UpdateWindowTitle(Path.GetFileName(fullFolder));
                toolStripStatusLabel1.Text = $"Opened folder {Path.GetFileName(fullFolder)} with {(_archive != null ? 1 : 0)} ARC and {_workspacePcks.Count} PCK file(s).";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to open folder: {ex.Message}", "Open Folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string? FindPreferredWorkspaceArc(string folderPath)
        {
            string dataDir = Path.Combine(folderPath, "Data");
            string preferred = Path.Combine(dataDir, "Media.arc");
            if (File.Exists(preferred))
                return preferred;

            return Directory.Exists(dataDir)
                ? Directory.EnumerateFiles(dataDir, "*.arc", SearchOption.AllDirectories).FirstOrDefault()
                : Directory.EnumerateFiles(folderPath, "*.arc", SearchOption.AllDirectories).FirstOrDefault();
        }

        private static IEnumerable<string> FindWorkspacePckFiles(string folderPath)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pckPath in Directory.EnumerateFiles(folderPath, "*.pck", SearchOption.AllDirectories))
                paths.Add(Path.GetFullPath(pckPath));

            return paths.OrderBy(path => path.Contains(Path.DirectorySeparatorChar + "Data" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(Path.GetFileName);
        }

        private static string GetWorkspacePckNodeTag(string path)
        {
            return WorkspacePckTagPrefix + Path.GetFullPath(path);
        }

        private bool TryGetWorkspacePckPathFromTag(string? tag, out string path)
        {
            path = string.Empty;
            if (string.IsNullOrEmpty(tag) || !tag.StartsWith(WorkspacePckTagPrefix, StringComparison.Ordinal))
                return false;

            path = tag.Substring(WorkspacePckTagPrefix.Length);
            return _workspacePcks.ContainsKey(path);
        }

        private void SelectWorkspacePck(string path)
        {
            if (!_workspacePcks.TryGetValue(path, out WorkspacePckContext? workspacePck))
                return;

            _pckFile = workspacePck.File;
            _pckFilePath = workspacePck.Path;
            _currentPckKey = null;

            PopulatePckTree(_pckFile);
            PopulatePckAssetList(_pckFile);
            tabControlMain.SelectedTab = tabPagePck;
            extractToolStripMenuItem.Enabled = false;
            replaceToolStripMenuItem.Enabled = false;
            deleteToolStripMenuItem.Enabled = false;
            editSwfToolStripMenuItem.Enabled = false;
            toolStripStatusLabel1.Text = $"Workspace PCK selected: {Path.GetFileName(path)}";
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
                _workspaceFolderPath = Path.GetDirectoryName(path);
                _workspacePcks.Clear();
                _workspacePcks[path] = new WorkspacePckContext { Path = path, File = _pckFile };
                _currentPckKey = null;

                if (_archive == null)
                    _mode = EditorMode.Pck;
                PopulatePckTree(_pckFile);
                PopulatePckAssetList(_pckFile);
                tabControlMain.SelectedTab = tabPagePck;

                if (_archive == null)
                {
                    treeViewArchive.Nodes.Clear();
                    EnsureWorkspacePckNodes();
                }
                else
                {
                    EnsureWorkspacePckNodes();
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

            EnsureWorkspacePckNodes();
            treeViewArchive.EndUpdate();
        }

        private void EnsureWorkspacePckNodes()
        {
            if (_workspacePcks.Count == 0)
                return;

            TreeNode? root = treeViewArchive.Nodes.Find(WorkspacePcksRootTag, false).FirstOrDefault();
            if (root == null)
            {
                root = new TreeNode("Workspace PCKs")
                {
                    Name = WorkspacePcksRootTag,
                    Tag = WorkspacePcksRootTag
                };
                treeViewArchive.Nodes.Add(root);
            }

            root.Nodes.Clear();
            foreach (var workspacePck in _workspacePcks.Values.OrderBy(p => Path.GetFileName(p.Path)))
            {
                string relativePath = !string.IsNullOrEmpty(_workspaceFolderPath)
                    ? Path.GetRelativePath(_workspaceFolderPath, workspacePck.Path)
                    : Path.GetFileName(workspacePck.Path);

                root.Nodes.Add(new TreeNode(relativePath)
                {
                    Name = GetWorkspacePckNodeTag(workspacePck.Path),
                    Tag = GetWorkspacePckNodeTag(workspacePck.Path)
                });
            }

            root.Expand();
        }

        private void ClearArchive()
        {
            _archive = null;
            _archivePath = null;
            _workspaceFolderPath = null;
            _workspacePcks.Clear();
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
            if (TryGetWorkspacePckPathFromTag(e.Node?.Tag as string, out string workspacePckPath))
            {
                SelectWorkspacePck(workspacePckPath);
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

            if (IsLanguagesLocAsset(asset))
            {
                EditLanguagesLocHex(asset);
                return;
            }

            string ext = Path.GetExtension(asset.Filename).ToLowerInvariant();
            if (IsImageExtension(ext))
            {
                listViewPckAssets_SelectedIndexChanged(sender, e);
                OpenImageAssetEditor(asset);
            }
            else
            {
                ExtractSelectedPckAsset();
            }
        }

        private void OpenImageAssetEditor(PckAsset asset)
        {
            try
            {
                using var sourceStream = new MemoryStream(asset.Data, writable: false);
                using var sourceImage = Image.FromStream(sourceStream);
                using var initialBitmap = new Bitmap(sourceImage);

                using var dialog = new Form
                {
                    Text = $"Image Editor - {asset.Filename}",
                    Width = 1200,
                    Height = 820,
                    StartPosition = FormStartPosition.CenterParent,
                    MinimizeBox = false,
                    KeyPreview = true,
                    BackColor = SurfaceColor,
                    ForeColor = ForegroundColor,
                    Font = CreateFriendlyFont(9.25f, FontStyle.Regular)
                };

                var toolbar = new FlowLayoutPanel
                {
                    Dock = DockStyle.Top,
                    Height = 48,
                    Padding = new Padding(10, 8, 10, 8),
                    BackColor = SurfaceAltColor,
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false
                };

                var btnBrushColor = new Button
                {
                    Text = "Brush Color",
                    Width = 110,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = AccentColor,
                    ForeColor = Color.White
                };
                btnBrushColor.FlatAppearance.BorderColor = AccentColor;

                var brushSizeLabel = new Label
                {
                    Text = "Brush:",
                    Width = 50,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(6, 8, 0, 0)
                };

                var brushSize = new NumericUpDown
                {
                    Minimum = 1,
                    Maximum = 64,
                    Value = 1,
                    Width = 70
                };

                var toolLabel = new Label
                {
                    Text = "Tool:",
                    Width = 40,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(6, 8, 0, 0)
                };

                var toolSelector = new ComboBox
                {
                    Width = 130,
                    DropDownStyle = ComboBoxStyle.DropDownList
                };
                toolSelector.Items.Add("Pencil");
                toolSelector.Items.Add("Brush");
                toolSelector.Items.Add("Eyedropper");
                toolSelector.Items.Add("Eraser");
                toolSelector.Items.Add("Move Paste");
                toolSelector.Items.Add("Hand");
                toolSelector.SelectedIndex = 0;

                var btnUndo = new Button
                {
                    Text = "Undo",
                    Width = 80,
                    FlatStyle = FlatStyle.Flat
                };
                btnUndo.FlatAppearance.BorderColor = BorderColor;

                var btnOpenWith = new Button
                {
                    Text = "Open Photos",
                    Width = 110,
                    FlatStyle = FlatStyle.Flat
                };
                btnOpenWith.FlatAppearance.BorderColor = BorderColor;

                var btnPaste = new Button
                {
                    Text = "Paste",
                    Width = 80,
                    FlatStyle = FlatStyle.Flat
                };
                btnPaste.FlatAppearance.BorderColor = BorderColor;

                var btnCommitPaste = new Button
                {
                    Text = "Commit Paste",
                    Width = 110,
                    FlatStyle = FlatStyle.Flat
                };
                btnCommitPaste.FlatAppearance.BorderColor = BorderColor;

                var btnRotateLeft = new Button
                {
                    Text = "Rotate L",
                    Width = 90,
                    FlatStyle = FlatStyle.Flat
                };
                btnRotateLeft.FlatAppearance.BorderColor = BorderColor;

                var btnRotateRight = new Button
                {
                    Text = "Rotate R",
                    Width = 90,
                    FlatStyle = FlatStyle.Flat
                };
                btnRotateRight.FlatAppearance.BorderColor = BorderColor;

                var btnScaleDown = new Button
                {
                    Text = "Scale -",
                    Width = 85,
                    FlatStyle = FlatStyle.Flat
                };
                btnScaleDown.FlatAppearance.BorderColor = BorderColor;

                var btnScaleUp = new Button
                {
                    Text = "Scale +",
                    Width = 85,
                    FlatStyle = FlatStyle.Flat
                };
                btnScaleUp.FlatAppearance.BorderColor = BorderColor;

                var zoomLabel = new Label
                {
                    Text = "100%",
                    Width = 56,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(6, 8, 0, 0)
                };

                var pictureHost = new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(26, 32, 45),
                    Padding = new Padding(14)
                };

                var pictureBox = new PictureBox
                {
                    Dock = DockStyle.Fill,
                    SizeMode = PictureBoxSizeMode.Normal,
                    BackColor = Color.Black,
                    Cursor = Cursors.Cross
                };

                var footer = new Panel
                {
                    Dock = DockStyle.Bottom,
                    Height = 56,
                    Padding = new Padding(10, 10, 10, 10),
                    BackColor = SurfaceAltColor
                };

                var btnCancel = new Button
                {
                    Text = "Cancel",
                    DialogResult = DialogResult.Cancel,
                    Width = 100,
                    Dock = DockStyle.Right,
                    FlatStyle = FlatStyle.Flat
                };
                btnCancel.FlatAppearance.BorderColor = BorderColor;

                var btnApply = new Button
                {
                    Text = "Apply",
                    Width = 120,
                    Dock = DockStyle.Right,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = AccentColor,
                    ForeColor = Color.White
                };
                btnApply.FlatAppearance.BorderColor = AccentColor;

                var colorDialog = new ColorDialog { Color = Color.White };
                var history = new Stack<Bitmap>();
                var editBitmap = new Bitmap(initialBitmap.Width, initialBitmap.Height, PixelFormat.Format32bppArgb);
                using (var initGraphics = Graphics.FromImage(editBitmap))
                {
                    initGraphics.CompositingMode = CompositingMode.SourceCopy;
                    initGraphics.DrawImage(initialBitmap, 0, 0, initialBitmap.Width, initialBitmap.Height);
                }
                Color currentBrush = colorDialog.Color;
                ImageEditorTool currentTool = ImageEditorTool.Pencil;
                bool drawing = false;
                Point lastPoint = Point.Empty;
                Bitmap? pastedSourceLayer = null;
                Bitmap? pastedLayer = null;
                Point pastedLayerPosition = Point.Empty;
                bool draggingPastedLayer = false;
                Point pastedDragOffset = Point.Empty;
                float pastedLayerScale = 1f;
                int pastedLayerRotationQuarterTurns = 0;
                float zoomFactor = 1f;
                const float minZoom = 0.25f;
                const float maxZoom = 32f;
                PointF panOffset = PointF.Empty;
                bool panning = false;
                Point panStartPoint = Point.Empty;
                PointF panStartOffset = PointF.Empty;

                void RefreshImage() => pictureBox.Invalidate();

                void UpdateZoomLabel()
                {
                    zoomLabel.Text = $"{Math.Round(zoomFactor * 100f)}%";
                }

                float GetBaseScale()
                {
                    if (editBitmap.Width <= 0 || editBitmap.Height <= 0 || pictureBox.ClientSize.Width <= 0 || pictureBox.ClientSize.Height <= 0)
                        return 1f;

                    float sx = pictureBox.ClientSize.Width / (float)editBitmap.Width;
                    float sy = pictureBox.ClientSize.Height / (float)editBitmap.Height;
                    return Math.Max(0.0001f, Math.Min(sx, sy));
                }

                RectangleF GetImageRectF()
                {
                    float scale = GetBaseScale() * zoomFactor;
                    float width = editBitmap.Width * scale;
                    float height = editBitmap.Height * scale;
                    float x = (pictureBox.ClientSize.Width - width) / 2f + panOffset.X;
                    float y = (pictureBox.ClientSize.Height - height) / 2f + panOffset.Y;
                    return new RectangleF(x, y, width, height);
                }

                bool TryMapViewPointToImage(Point point, out Point mapped)
                {
                    mapped = Point.Empty;
                    RectangleF imgRect = GetImageRectF();
                    if (imgRect.Width <= 0 || imgRect.Height <= 0)
                        return false;

                    if (point.X < imgRect.Left || point.X > imgRect.Right || point.Y < imgRect.Top || point.Y > imgRect.Bottom)
                        return false;

                    int x = (int)((point.X - imgRect.Left) * (editBitmap.Width / imgRect.Width));
                    int y = (int)((point.Y - imgRect.Top) * (editBitmap.Height / imgRect.Height));
                    x = Math.Max(0, Math.Min(editBitmap.Width - 1, x));
                    y = Math.Max(0, Math.Min(editBitmap.Height - 1, y));
                    mapped = new Point(x, y);
                    return true;
                }

                void SetCursorForTool()
                {
                    if (currentTool == ImageEditorTool.Hand)
                        pictureBox.Cursor = panning ? Cursors.SizeAll : Cursors.Hand;
                    else if (currentTool == ImageEditorTool.MovePastedLayer)
                        pictureBox.Cursor = Cursors.SizeAll;
                    else if (currentTool == ImageEditorTool.Eyedropper)
                        pictureBox.Cursor = Cursors.UpArrow;
                    else
                        pictureBox.Cursor = Cursors.Cross;
                }

                void ZoomAt(float factor, Point focusPoint)
                {
                    float oldZoom = zoomFactor;
                    float newZoom = Math.Max(minZoom, Math.Min(maxZoom, zoomFactor * factor));
                    if (Math.Abs(newZoom - oldZoom) < 0.0001f)
                        return;

                    RectangleF oldRect = GetImageRectF();
                    if (oldRect.Width <= 0 || oldRect.Height <= 0)
                    {
                        zoomFactor = newZoom;
                        UpdateZoomLabel();
                        RefreshImage();
                        return;
                    }

                    float relX = (focusPoint.X - oldRect.Left) / oldRect.Width;
                    float relY = (focusPoint.Y - oldRect.Top) / oldRect.Height;
                    relX = Math.Max(0f, Math.Min(1f, relX));
                    relY = Math.Max(0f, Math.Min(1f, relY));

                    zoomFactor = newZoom;
                    float newScale = GetBaseScale() * zoomFactor;
                    float newW = editBitmap.Width * newScale;
                    float newH = editBitmap.Height * newScale;

                    float centerX = (pictureBox.ClientSize.Width - newW) / 2f;
                    float centerY = (pictureBox.ClientSize.Height - newH) / 2f;
                    panOffset = new PointF(
                        focusPoint.X - centerX - relX * newW,
                        focusPoint.Y - centerY - relY * newH);

                    UpdateZoomLabel();
                    RefreshImage();
                }

                void NudgePastedLayer(int dx, int dy)
                {
                    if (pastedLayer == null)
                        return;

                    pastedLayerPosition = new Point(
                        Math.Max(0, Math.Min(editBitmap.Width - pastedLayer.Width, pastedLayerPosition.X + dx)),
                        Math.Max(0, Math.Min(editBitmap.Height - pastedLayer.Height, pastedLayerPosition.Y + dy)));
                    RefreshImage();
                }

                void ClampPastedLayerPosition()
                {
                    if (pastedLayer == null)
                        return;

                    pastedLayerPosition = new Point(
                        Math.Max(0, Math.Min(editBitmap.Width - pastedLayer.Width, pastedLayerPosition.X)),
                        Math.Max(0, Math.Min(editBitmap.Height - pastedLayer.Height, pastedLayerPosition.Y)));
                }

                void RebuildPastedLayer(bool keepCenter = true)
                {
                    if (pastedSourceLayer == null)
                    {
                        pastedLayer?.Dispose();
                        pastedLayer = null;
                        RefreshImage();
                        return;
                    }

                    Point oldCenter = pastedLayer == null
                        ? new Point(editBitmap.Width / 2, editBitmap.Height / 2)
                        : new Point(pastedLayerPosition.X + pastedLayer.Width / 2, pastedLayerPosition.Y + pastedLayer.Height / 2);

                    Bitmap oriented = new Bitmap(pastedSourceLayer);
                    if (pastedLayerRotationQuarterTurns != 0)
                    {
                        RotateFlipType rotateType = pastedLayerRotationQuarterTurns switch
                        {
                            1 => RotateFlipType.Rotate90FlipNone,
                            2 => RotateFlipType.Rotate180FlipNone,
                            3 => RotateFlipType.Rotate270FlipNone,
                            _ => RotateFlipType.RotateNoneFlipNone
                        };
                        oriented.RotateFlip(rotateType);
                    }

                    int maxWidth = Math.Max(1, editBitmap.Width * 4);
                    int maxHeight = Math.Max(1, editBitmap.Height * 4);
                    int newW = Math.Max(1, Math.Min(maxWidth, (int)Math.Round(oriented.Width * pastedLayerScale)));
                    int newH = Math.Max(1, Math.Min(maxHeight, (int)Math.Round(oriented.Height * pastedLayerScale)));

                    Bitmap transformed = new(newW, newH, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(transformed))
                    {
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.DrawImage(oriented, new Rectangle(0, 0, newW, newH), new Rectangle(0, 0, oriented.Width, oriented.Height), GraphicsUnit.Pixel);
                    }
                    oriented.Dispose();

                    pastedLayer?.Dispose();
                    pastedLayer = transformed;

                    if (keepCenter)
                    {
                        pastedLayerPosition = new Point(
                            Math.Max(0, oldCenter.X - pastedLayer.Width / 2),
                            Math.Max(0, oldCenter.Y - pastedLayer.Height / 2));
                    }

                    ClampPastedLayerPosition();

                    RefreshImage();
                }

                void PushUndo()
                {
                    history.Push(new Bitmap(editBitmap));
                    if (history.Count > 25)
                    {
                        // Keep memory usage bounded for large textures.
                        var trimmed = history.Reverse().Take(25).Reverse().ToList();
                        history.Clear();
                        foreach (var bmp in trimmed)
                            history.Push(bmp);
                    }
                }

                btnBrushColor.Click += (s, e) =>
                {
                    if (colorDialog.ShowDialog(dialog) == DialogResult.OK)
                    {
                        currentBrush = colorDialog.Color;
                        btnBrushColor.BackColor = currentBrush;
                    }
                };

                toolSelector.SelectedIndexChanged += (s, e) =>
                {
                    currentTool = toolSelector.SelectedIndex switch
                    {
                        0 => ImageEditorTool.Pencil,
                        1 => ImageEditorTool.Brush,
                        2 => ImageEditorTool.Eyedropper,
                        3 => ImageEditorTool.Eraser,
                        4 => ImageEditorTool.MovePastedLayer,
                        5 => ImageEditorTool.Hand,
                        _ => ImageEditorTool.Pencil
                    };
                    SetCursorForTool();
                };

                btnUndo.Click += (s, e) =>
                {
                    if (history.Count == 0)
                        return;

                    editBitmap.Dispose();
                    editBitmap = history.Pop();
                    RefreshImage();
                };

                btnOpenWith.Click += (s, e) =>
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "LegacyConsolePackEditor", "pck-editor");
                    Directory.CreateDirectory(tempDir);
                    string tempFile = Path.Combine(tempDir, Path.GetFileName(asset.Filename));
                    SaveBitmapByExtension(editBitmap, tempFile, asset.Filename);

                    if (TryLaunchPhotosApp(tempFile, out var photosError))
                        toolStripStatusLabel1.Text = $"Opened {asset.Filename} in Photos.";
                    else
                        MessageBox.Show(dialog, $"Failed to open Photos app: {photosError}", "Open Photos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                };

                bool TryLoadClipboardLayer()
                {
                    try
                    {
                        using var clipboardImg = GetClipboardImagePreserveAlpha();
                        if (clipboardImg == null)
                            return false;

                        pastedSourceLayer?.Dispose();
                        pastedLayer?.Dispose();
                        pastedSourceLayer = new Bitmap(clipboardImg);
                        pastedLayerScale = 1f;
                        pastedLayerRotationQuarterTurns = 0;
                        pastedLayerPosition = new Point(
                            Math.Max(0, (editBitmap.Width - pastedSourceLayer.Width) / 2),
                            Math.Max(0, (editBitmap.Height - pastedSourceLayer.Height) / 2));
                        RebuildPastedLayer(keepCenter: false);
                        currentTool = ImageEditorTool.MovePastedLayer;
                        toolSelector.SelectedIndex = 4;
                        SetCursorForTool();
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }

                btnPaste.Click += (s, e) =>
                {
                    if (!TryLoadClipboardLayer())
                        MessageBox.Show(dialog, "Clipboard does not contain an image.", "Paste", MessageBoxButtons.OK, MessageBoxIcon.Information);
                };

                btnCommitPaste.Click += (s, e) =>
                {
                    if (pastedLayer == null)
                        return;

                    PushUndo();
                    using var g = Graphics.FromImage(editBitmap);
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.DrawImage(pastedLayer, pastedLayerPosition);

                    pastedSourceLayer?.Dispose();
                    pastedSourceLayer = null;
                    pastedLayer.Dispose();
                    pastedLayer = null;
                    pastedLayerScale = 1f;
                    pastedLayerRotationQuarterTurns = 0;
                    currentTool = ImageEditorTool.Brush;
                    toolSelector.SelectedIndex = 0;
                    SetCursorForTool();
                    RefreshImage();
                };

                btnRotateLeft.Click += (s, e) =>
                {
                    if (pastedSourceLayer == null)
                        return;

                    pastedLayerRotationQuarterTurns = (pastedLayerRotationQuarterTurns + 3) % 4;
                    RebuildPastedLayer();
                };

                btnRotateRight.Click += (s, e) =>
                {
                    if (pastedSourceLayer == null)
                        return;

                    pastedLayerRotationQuarterTurns = (pastedLayerRotationQuarterTurns + 1) % 4;
                    RebuildPastedLayer();
                };

                btnScaleDown.Click += (s, e) =>
                {
                    if (pastedSourceLayer == null)
                        return;

                    pastedLayerScale = Math.Max(0.05f, pastedLayerScale * 0.9f);
                    RebuildPastedLayer();
                };
                btnScaleUp.Click += (s, e) =>
                {
                    if (pastedSourceLayer == null)
                        return;

                    pastedLayerScale = Math.Min(8f, pastedLayerScale * 1.1f);
                    RebuildPastedLayer();
                };

                dialog.KeyDown += (s, e) =>
                {
                    if (e.Control && e.KeyCode == Keys.V)
                    {
                        if (!TryLoadClipboardLayer())
                            MessageBox.Show(dialog, "Clipboard does not contain an image.", "Paste", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        e.Handled = true;
                        return;
                    }

                    if (e.Control && (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus))
                    {
                        ZoomAt(1.15f, new Point(pictureBox.ClientSize.Width / 2, pictureBox.ClientSize.Height / 2));
                        e.Handled = true;
                        return;
                    }

                    if (e.Control && (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus))
                    {
                        ZoomAt(1f / 1.15f, new Point(pictureBox.ClientSize.Width / 2, pictureBox.ClientSize.Height / 2));
                        e.Handled = true;
                        return;
                    }

                    if (pastedLayer != null)
                    {
                        int step = e.Shift ? 10 : 1;
                        if (e.KeyCode == Keys.Left)
                        {
                            NudgePastedLayer(-step, 0);
                            e.Handled = true;
                            return;
                        }
                        if (e.KeyCode == Keys.Right)
                        {
                            NudgePastedLayer(step, 0);
                            e.Handled = true;
                            return;
                        }
                        if (e.KeyCode == Keys.Up)
                        {
                            NudgePastedLayer(0, -step);
                            e.Handled = true;
                            return;
                        }
                        if (e.KeyCode == Keys.Down)
                        {
                            NudgePastedLayer(0, step);
                            e.Handled = true;
                            return;
                        }
                    }
                };

                pictureBox.MouseWheel += (s, e) =>
                {
                    if ((ModifierKeys & Keys.Control) == Keys.Control)
                    {
                        float factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
                        ZoomAt(factor, e.Location);
                        return;
                    }

                    int panStep = Math.Sign(e.Delta) * 42;
                    if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                        panOffset = new PointF(panOffset.X + panStep, panOffset.Y);
                    else
                        panOffset = new PointF(panOffset.X, panOffset.Y + panStep);

                    RefreshImage();
                };

                pictureBox.Paint += (s, e) =>
                {
                    RectangleF imageRectF = GetImageRectF();
                    Rectangle imgRect = Rectangle.Round(imageRectF);
                    if (imgRect.Width <= 0 || imgRect.Height <= 0)
                        return;

                    DrawCheckerboard(e.Graphics, imgRect, 12);
                    e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                    e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    e.Graphics.DrawImage(editBitmap, imgRect);

                    if (pastedLayer != null)
                    {
                        float sx = imageRectF.Width / editBitmap.Width;
                        float sy = imageRectF.Height / editBitmap.Height;
                        Rectangle pastedRect = Rectangle.Round(new RectangleF(
                            imageRectF.Left + pastedLayerPosition.X * sx,
                            imageRectF.Top + pastedLayerPosition.Y * sy,
                            Math.Max(1f, pastedLayer.Width * sx),
                            Math.Max(1f, pastedLayer.Height * sy)));
                        e.Graphics.DrawImage(pastedLayer, pastedRect);

                        using var outline = new Pen(Color.FromArgb(220, 66, 144, 255), 2f);
                        e.Graphics.DrawRectangle(outline, pastedRect);
                    }
                };

                pictureBox.MouseDown += (s, e) =>
                {
                    if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && currentTool == ImageEditorTool.Hand))
                    {
                        panning = true;
                        panStartPoint = e.Location;
                        panStartOffset = panOffset;
                        SetCursorForTool();
                        return;
                    }

                    if (e.Button != MouseButtons.Left)
                        return;

                    if (!TryMapViewPointToImage(e.Location, out var mapped))
                        return;

                    if (currentTool == ImageEditorTool.Eyedropper)
                    {
                        currentBrush = editBitmap.GetPixel(mapped.X, mapped.Y);
                        btnBrushColor.BackColor = currentBrush;
                        currentTool = ImageEditorTool.Pencil;
                        toolSelector.SelectedIndex = 0;
                        return;
                    }

                    if (currentTool == ImageEditorTool.MovePastedLayer && pastedLayer != null)
                    {
                        var layerRect = new Rectangle(pastedLayerPosition, pastedLayer.Size);
                        if (layerRect.Contains(mapped))
                        {
                            draggingPastedLayer = true;
                            pastedDragOffset = new Point(mapped.X - pastedLayerPosition.X, mapped.Y - pastedLayerPosition.Y);
                            return;
                        }
                    }

                    bool isBrush = currentTool == ImageEditorTool.Brush;
                    bool isPencil = currentTool == ImageEditorTool.Pencil;
                    bool isEraser = currentTool == ImageEditorTool.Eraser;

                    if (!isBrush && !isPencil && !isEraser)
                        return;

                    PushUndo();
                    drawing = true;
                    lastPoint = mapped;

                    if (isBrush)
                    {
                        using var g = Graphics.FromImage(editBitmap);
                        using var pen = new Pen(currentBrush, (float)brushSize.Value)
                        {
                            StartCap = LineCap.Round,
                            EndCap = LineCap.Round,
                            LineJoin = LineJoin.Round
                        };
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.DrawLine(pen, mapped, mapped);
                    }
                    else
                    {
                        DrawPixelLine(editBitmap, mapped, mapped, currentBrush, (int)brushSize.Value, isEraser);
                    }

                    RefreshImage();
                };

                pictureBox.MouseMove += (s, e) =>
                {
                    if (panning)
                    {
                        panOffset = new PointF(
                            panStartOffset.X + (e.Location.X - panStartPoint.X),
                            panStartOffset.Y + (e.Location.Y - panStartPoint.Y));
                        RefreshImage();
                        return;
                    }

                    if (draggingPastedLayer && pastedLayer != null)
                    {
                        if (!TryMapViewPointToImage(e.Location, out var movedPoint))
                            return;

                        pastedLayerPosition = new Point(
                            Math.Max(0, Math.Min(editBitmap.Width - pastedLayer.Width, movedPoint.X - pastedDragOffset.X)),
                            Math.Max(0, Math.Min(editBitmap.Height - pastedLayer.Height, movedPoint.Y - pastedDragOffset.Y)));
                        RefreshImage();
                        return;
                    }

                    if (!drawing)
                        return;

                    bool isBrush = currentTool == ImageEditorTool.Brush;
                    bool isPencil = currentTool == ImageEditorTool.Pencil;
                    bool isEraser = currentTool == ImageEditorTool.Eraser;

                    if (!isBrush && !isPencil && !isEraser)
                        return;

                    if (!TryMapViewPointToImage(e.Location, out var mapped))
                        return;

                    if (isBrush)
                    {
                        using var g = Graphics.FromImage(editBitmap);
                        using var pen = new Pen(currentBrush, (float)brushSize.Value)
                        {
                            StartCap = LineCap.Round,
                            EndCap = LineCap.Round,
                            LineJoin = LineJoin.Round
                        };
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.DrawLine(pen, lastPoint, mapped);
                    }
                    else
                    {
                        DrawPixelLine(editBitmap, lastPoint, mapped, currentBrush, (int)brushSize.Value, isEraser);
                    }

                    lastPoint = mapped;
                    RefreshImage();
                };

                pictureBox.MouseUp += (s, e) =>
                {
                    drawing = false;
                    draggingPastedLayer = false;
                    panning = false;
                    SetCursorForTool();
                };

                pictureBox.MouseEnter += (s, e) => pictureBox.Focus();
                pictureBox.Resize += (s, e) => RefreshImage();

                btnApply.Click += (s, e) =>
                {
                    using var ms = new MemoryStream();
                    SaveBitmapByExtension(editBitmap, ms, asset.Filename);

                    asset.SetData(ms.ToArray());
                    PersistPckChanges();
                    SelectPckAsset(asset);
                    toolStripStatusLabel1.Text = $"Updated image asset {asset.Filename} in built-in editor.";
                    dialog.DialogResult = DialogResult.OK;
                    dialog.Close();
                };

                footer.Controls.Add(btnCancel);
                footer.Controls.Add(btnApply);

                toolbar.Controls.Add(btnBrushColor);
                toolbar.Controls.Add(brushSizeLabel);
                toolbar.Controls.Add(brushSize);
                toolbar.Controls.Add(toolLabel);
                toolbar.Controls.Add(toolSelector);
                toolbar.Controls.Add(btnUndo);
                toolbar.Controls.Add(btnPaste);
                toolbar.Controls.Add(btnCommitPaste);
                toolbar.Controls.Add(btnRotateLeft);
                toolbar.Controls.Add(btnRotateRight);
                toolbar.Controls.Add(btnScaleDown);
                toolbar.Controls.Add(btnScaleUp);
                toolbar.Controls.Add(zoomLabel);
                toolbar.Controls.Add(btnOpenWith);

                pictureHost.Controls.Add(pictureBox);
                dialog.Controls.Add(pictureHost);
                dialog.Controls.Add(footer);
                dialog.Controls.Add(toolbar);

                UpdateZoomLabel();
                SetCursorForTool();
                RefreshImage();
                _ = dialog.ShowDialog(this);

                editBitmap.Dispose();
                pastedSourceLayer?.Dispose();
                pastedLayer?.Dispose();
                foreach (var old in history)
                    old.Dispose();
                history.Clear();
                return;

            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to open image editor: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

            if (IsLanguagesLocAsset(asset))
            {
                EditLanguagesLocHex(asset);
                return;
            }

            if (!IsImageExtension(Path.GetExtension(asset.Filename).ToLowerInvariant()))
            {
                ExtractSelectedPckAsset();
                return;
            }

            OpenImageAssetEditor(asset);
        }

        private static void SaveBitmapByExtension(Bitmap bitmap, string targetPath, string filename)
        {
            string ext = Path.GetExtension(filename).ToLowerInvariant();
            ImageFormat format = ext switch
            {
                ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                ".bmp" => ImageFormat.Bmp,
                ".gif" => ImageFormat.Gif,
                _ => ImageFormat.Png
            };

            bitmap.Save(targetPath, format);
        }

        private static void SaveBitmapByExtension(Bitmap bitmap, Stream stream, string filename)
        {
            string ext = Path.GetExtension(filename).ToLowerInvariant();
            ImageFormat format = ext switch
            {
                ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                ".bmp" => ImageFormat.Bmp,
                ".gif" => ImageFormat.Gif,
                _ => ImageFormat.Png
            };

            bitmap.Save(stream, format);
        }

        private static Bitmap? GetClipboardImagePreserveAlpha()
        {
            try
            {
                var dataObject = Clipboard.GetDataObject();
                if (dataObject != null && dataObject.GetDataPresent("PNG"))
                {
                    var pngData = dataObject.GetData("PNG");
                    if (pngData is MemoryStream pngStream)
                    {
                        byte[] bytes = pngStream.ToArray();
                        using var ms = new MemoryStream(bytes, writable: false);
                        using var img = Image.FromStream(ms);
                        return new Bitmap(img);
                    }
                }
            }
            catch
            {
                // Fall back to regular clipboard image access.
            }

            if (!Clipboard.ContainsImage())
                return null;

            using var fallback = Clipboard.GetImage();
            return fallback == null ? null : new Bitmap(fallback);
        }

        private static void DrawPixelLine(Bitmap bitmap, Point a, Point b, Color color, int size, bool eraseToTransparent)
        {
            int x0 = a.X;
            int y0 = a.Y;
            int x1 = b.X;
            int y1 = b.Y;

            int dx = Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            int drawSize = Math.Max(1, size);
            int radius = drawSize / 2;
            Color drawColor = eraseToTransparent ? Color.FromArgb(0, 0, 0, 0) : color;

            while (true)
            {
                DrawPixelStamp(bitmap, x0, y0, radius, drawColor);
                if (x0 == x1 && y0 == y1)
                    break;

                int e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }
                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private static void DrawPixelStamp(Bitmap bitmap, int centerX, int centerY, int radius, Color color)
        {
            for (int y = centerY - radius; y <= centerY + radius; y++)
            {
                if (y < 0 || y >= bitmap.Height)
                    continue;

                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    if (x < 0 || x >= bitmap.Width)
                        continue;

                    bitmap.SetPixel(x, y, color);
                }
            }
        }

        private static void DrawCheckerboard(Graphics graphics, Rectangle area, int squareSize)
        {
            using var light = new SolidBrush(Color.White);
            using var dark = new SolidBrush(Color.FromArgb(208, 208, 208));

            graphics.FillRectangle(light, area);
            for (int y = area.Top; y < area.Bottom; y += squareSize)
            {
                for (int x = area.Left; x < area.Right; x += squareSize)
                {
                    bool drawDark = ((x - area.Left) / squareSize + (y - area.Top) / squareSize) % 2 == 0;
                    if (!drawDark)
                        continue;

                    int w = Math.Min(squareSize, area.Right - x);
                    int h = Math.Min(squareSize, area.Bottom - y);
                    graphics.FillRectangle(dark, x, y, w, h);
                }
            }
        }

        private static Rectangle ImageRectFromBitmapRect(Rectangle imageRectOnControl, Size bitmapSize, Rectangle bitmapRect)
        {
            double sx = imageRectOnControl.Width / (double)bitmapSize.Width;
            double sy = imageRectOnControl.Height / (double)bitmapSize.Height;

            int x = imageRectOnControl.Left + (int)Math.Round(bitmapRect.Left * sx);
            int y = imageRectOnControl.Top + (int)Math.Round(bitmapRect.Top * sy);
            int w = Math.Max(1, (int)Math.Round(bitmapRect.Width * sx));
            int h = Math.Max(1, (int)Math.Round(bitmapRect.Height * sy));
            return new Rectangle(x, y, w, h);
        }

        private static bool TryGetImageDrawRectangle(PictureBox pictureBox, Size bitmapSize, out Rectangle imageRect)
        {
            imageRect = Rectangle.Empty;
            if (pictureBox.ClientSize.Width <= 0 || pictureBox.ClientSize.Height <= 0 || bitmapSize.Width <= 0 || bitmapSize.Height <= 0)
                return false;

            float imageRatio = bitmapSize.Width / (float)bitmapSize.Height;
            float boxRatio = pictureBox.ClientSize.Width / (float)pictureBox.ClientSize.Height;

            int drawWidth;
            int drawHeight;
            int offsetX;
            int offsetY;

            if (imageRatio > boxRatio)
            {
                drawWidth = pictureBox.ClientSize.Width;
                drawHeight = (int)(drawWidth / imageRatio);
                offsetX = 0;
                offsetY = (pictureBox.ClientSize.Height - drawHeight) / 2;
            }
            else
            {
                drawHeight = pictureBox.ClientSize.Height;
                drawWidth = (int)(drawHeight * imageRatio);
                offsetY = 0;
                offsetX = (pictureBox.ClientSize.Width - drawWidth) / 2;
            }

            imageRect = new Rectangle(offsetX, offsetY, Math.Max(1, drawWidth), Math.Max(1, drawHeight));
            return true;
        }

        private static bool TryMapPictureBoxPointToImage(PictureBox pictureBox, Bitmap bitmap, Point point, out Point mapped)
        {
            mapped = Point.Empty;
            if (!TryGetImageDrawRectangle(pictureBox, bitmap.Size, out var imageRect))
                return false;

            if (!imageRect.Contains(point))
                return false;

            int x = (int)((point.X - imageRect.Left) * (bitmap.Width / (double)imageRect.Width));
            int y = (int)((point.Y - imageRect.Top) * (bitmap.Height / (double)imageRect.Height));
            x = Math.Max(0, Math.Min(bitmap.Width - 1, x));
            y = Math.Max(0, Math.Min(bitmap.Height - 1, y));

            mapped = new Point(x, y);
            return true;
        }

        private static bool TryLaunchMsPaint(string filePath, out string error)
        {
            error = "unknown";

            string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string[] candidates =
            {
                Path.Combine(windowsDir, "System32", "mspaint.exe"),
                Path.Combine(windowsDir, "Sysnative", "mspaint.exe"),
                Path.Combine(windowsDir, "System32", "pbrush.exe"),
                "mspaint.exe"
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    if (Path.IsPathRooted(candidate) && !File.Exists(candidate))
                        continue;

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = candidate,
                        Arguments = $"\"{filePath}\"",
                        UseShellExecute = true
                    };

                    var process = System.Diagnostics.Process.Start(psi);
                    if (process != null)
                    {
                        // Some systems spawn Paint and immediately fail due to missing runtime DLLs.
                        // Treat quick exits as launch failure so we can use fallback behavior.
                        if (process.WaitForExit(1200))
                        {
                            error = $"{Path.GetFileName(candidate)} exited immediately (code {process.ExitCode})";
                            continue;
                        }

                        error = string.Empty;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }

            if (string.IsNullOrWhiteSpace(error))
                error = "no MS Paint executable was found";

            return false;
        }

        private static bool TryLaunchOpenWithDialog(string filePath, out string error)
        {
            error = "unknown";

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "rundll32.exe",
                    Arguments = $"shell32.dll,OpenAs_RunDLL \"{filePath}\"",
                    UseShellExecute = true
                };

                var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    error = string.Empty;
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (string.IsNullOrWhiteSpace(error))
                error = "failed to start Open With dialog";

            return false;
        }

        private static bool TryLaunchPhotosApp(string filePath, out string error)
        {
            error = "unknown";

            string fullPath = Path.GetFullPath(filePath);
            string uri = $"ms-photos:viewer?fileName={Uri.EscapeDataString(fullPath)}";

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = uri,
                    UseShellExecute = true
                };

                var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    error = string.Empty;
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (string.IsNullOrWhiteSpace(error))
                error = "failed to launch ms-photos URI";

            return false;
        }

        private static bool IsLanguagesLocAsset(PckAsset asset)
        {
            string fileName = Path.GetFileName(asset.Filename);
            return fileName.Equals("languages.loc", StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals("localisation.loc", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class EditableTextSegment
        {
            public int Offset { get; init; }
            public int Capacity { get; init; }
            public int TextOffset { get; init; }
            public int TextCapacity { get; init; }
            public int TextLength { get; init; }
            public bool HasLengthPrefix { get; init; }
            public string HiddenPrefix { get; init; } = string.Empty;
            public string Label { get; init; } = string.Empty;
            public string Text { get; set; } = string.Empty;
        }

        private sealed class RawLocSegment
        {
            public int Offset { get; init; }
            public int TextOffset { get; init; }
            public int TextCapacity { get; init; }
            public int TextLength { get; init; }
            public string Decoded { get; init; } = string.Empty;
        }

        private void EditLanguagesLocHex(PckAsset asset)
        {
            int originalSize = asset.Data.Length;

            using var dialog = new Form
            {
                Text = $"Hex Editor - {asset.Filename}",
                Width = 900,
                Height = 640,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = true,
                FormBorderStyle = FormBorderStyle.Sizable,
                BackColor = SurfaceColor,
                ForeColor = ForegroundColor,
                Font = CreateFriendlyFont(9.25f, FontStyle.Regular)
            };

            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 48,
                Padding = new Padding(12, 10, 12, 8),
                Text = $"Built-in LOC editor. File size stays fixed at {originalSize:N0} bytes. Text is written to fixed slots and padded with 00.",
                BackColor = SurfaceAltColor,
                ForeColor = ForegroundColor
            };

            var editorSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                Panel1MinSize = 0,
                Panel2MinSize = 0
            };

            dialog.Shown += (s, e) =>
            {
                int splitterWidth = editorSplit.SplitterWidth;
                int availableHeight = editorSplit.ClientSize.Height;

                int minTop = Math.Min(220, Math.Max(80, (availableHeight - splitterWidth) / 2));
                int minBottom = Math.Min(260, Math.Max(80, (availableHeight - splitterWidth) / 2));

                editorSplit.Panel1MinSize = minTop;
                editorSplit.Panel2MinSize = minBottom;

                int maxTop = availableHeight - minBottom - splitterWidth;

                if (maxTop < minTop)
                    return;

                int desiredTop = Math.Max(minTop, Math.Min(maxTop, (int)(availableHeight * 0.40)));
                editorSplit.SplitterDistance = desiredTop;
            };

            var textPreviewLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Text = "Text / ASCII preview",
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0),
                BackColor = SurfaceAltColor,
                ForeColor = ForegroundColor
            };

            var textPreviewBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                ReadOnly = true,
                WordWrap = false,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Point),
                BackColor = Color.White
            };

            var textGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoGenerateColumns = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                EnableHeadersVisualStyles = false
            };

            textGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Offset",
                HeaderText = "Offset",
                Width = 95,
                ReadOnly = true
            });
            textGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Bytes",
                HeaderText = "Info",
                Width = 210,
                ReadOnly = true
            });
            textGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Text",
                HeaderText = "Text",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                ReadOnly = false
            });

            var textTabHint = new Label
            {
                Dock = DockStyle.Top,
                Height = 32,
                Text = "Edit rows directly. Leading ESC / '=' markers are hidden in this view.",
                Padding = new Padding(8, 6, 8, 0),
                BackColor = SurfaceAltColor,
                ForeColor = ForegroundColor
            };

            var btnReloadTextRows = new Button
            {
                Text = "Reload Text Rows",
                Width = 160,
                Dock = DockStyle.Left,
                FlatStyle = FlatStyle.Flat
            };
            btnReloadTextRows.FlatAppearance.BorderColor = BorderColor;

            var textRows = ExtractEditableTextSegments(asset.Data);

            void PopulateTextRows(IEnumerable<EditableTextSegment> segments)
            {
                textGrid.Rows.Clear();
                foreach (var seg in segments)
                {
                    string info = string.IsNullOrWhiteSpace(seg.Label)
                        ? $"{seg.TextCapacity} bytes"
                        : $"{seg.Label} [{seg.TextCapacity}]";
                    int rowIndex = textGrid.Rows.Add($"0x{seg.Offset:X6}", info, EncodeGridText(seg.Text));
                    textGrid.Rows[rowIndex].Tag = seg;
                }
            }

            PopulateTextRows(textRows);

            btnReloadTextRows.Click += (s, e) =>
            {
                var refreshed = ExtractEditableTextSegments(asset.Data);
                PopulateTextRows(refreshed);
                textPreviewBox.Text = BuildAsciiPreview(asset.Data);
                toolStripStatusLabel1.Text = $"Reloaded {refreshed.Count} text rows.";
            };

            textPreviewBox.Text = BuildAsciiPreview(asset.Data);

            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = SurfaceAltColor,
                Padding = new Padding(12, 10, 12, 10)
            };

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Width = 110,
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat
            };
            btnCancel.FlatAppearance.BorderColor = BorderColor;

            var btnApply = new Button
            {
                Text = "Apply",
                Width = 130,
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentColor,
                ForeColor = Color.White
            };
            btnApply.FlatAppearance.BorderColor = AccentColor;

            btnApply.Click += (s, e) =>
            {
                try
                {
                    byte[] resized = ResizeToFixedLength(asset.Data.ToArray(), originalSize);

                    int truncatedCount = 0;
                    foreach (DataGridViewRow row in textGrid.Rows)
                    {
                        if (row.Tag is not EditableTextSegment seg)
                            continue;

                        string newText = DecodeGridText((row.Cells[2].Value?.ToString() ?? string.Empty).Replace("\r\n", "\n"));
                        if (WriteUtf8IntoSegment(resized, seg, newText))
                            truncatedCount++;
                    }

                    asset.SetData(resized);
                    PersistPckChanges();

                    foreach (ListViewItem item in listViewPckAssets.Items)
                    {
                        if (ReferenceEquals(item.Tag, asset))
                        {
                            item.SubItems[1].Text = asset.Size.ToString("N0");
                            break;
                        }
                    }

                    SelectPckAsset(asset);
                    toolStripStatusLabel1.Text = truncatedCount == 0
                        ? $"Updated {asset.Filename} via built-in LOC editor ({originalSize:N0} bytes fixed)."
                        : $"Updated {asset.Filename}; {truncatedCount} text value(s) were truncated to fit fixed slots.";
                    dialog.DialogResult = DialogResult.OK;
                    dialog.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(dialog, $"Invalid hex data: {ex.Message}", "Hex Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            footerPanel.Controls.Add(btnCancel);
            footerPanel.Controls.Add(btnApply);
            footerPanel.Controls.Add(btnReloadTextRows);

            editorSplit.Panel1.Controls.Add(textPreviewBox);
            editorSplit.Panel1.Controls.Add(textPreviewLabel);
            editorSplit.Panel2.Controls.Add(textGrid);
            editorSplit.Panel2.Controls.Add(textTabHint);

            dialog.Controls.Add(editorSplit);
            dialog.Controls.Add(footerPanel);
            dialog.Controls.Add(header);

            _ = dialog.ShowDialog(this);
        }

        private static string FormatHexBytes(byte[] bytes)
        {
            if (bytes.Length == 0)
                return string.Empty;

            var sb = new StringBuilder(bytes.Length * 3);
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append(bytes[i].ToString("X2"));

                bool isLineEnd = (i + 1) % 16 == 0;
                if (i + 1 < bytes.Length)
                    sb.Append(isLineEnd ? Environment.NewLine : " ");
            }

            return sb.ToString();
        }

        private static byte[] ParseHexInput(string input)
        {
            string cleaned = Regex.Replace(input ?? string.Empty, "[^0-9A-Fa-f]", string.Empty);
            if (cleaned.Length == 0)
                return Array.Empty<byte>();

            if (cleaned.Length % 2 != 0)
                cleaned += "0";

            byte[] bytes = new byte[cleaned.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                string hexByte = cleaned.Substring(i * 2, 2);
                bytes[i] = Convert.ToByte(hexByte, 16);
            }

            return bytes;
        }

        private static byte[] ResizeToFixedLength(byte[] data, int fixedLength)
        {
            if (fixedLength < 0)
                throw new ArgumentOutOfRangeException(nameof(fixedLength));

            if (data.Length == fixedLength)
                return data;

            byte[] resized = new byte[fixedLength];
            int copyLength = Math.Min(data.Length, fixedLength);
            Buffer.BlockCopy(data, 0, resized, 0, copyLength);
            return resized;
        }

        private void SetupPckTempWatcher(string tempFile, PckAsset asset)
        {
            if (_pckTempWatcher != null)
            {
                _pckTempWatcher.Dispose();
                _pckTempWatcher = null;
            }

            _pckTempWatcher = new FileSystemWatcher(Path.GetDirectoryName(tempFile) ?? string.Empty)
            {
                Filter = Path.GetFileName(tempFile),
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };

            _pckTempWatcher.Changed += (s, e) =>
            {
                try
                {
                    if (!File.Exists(tempFile))
                        return;

                    byte[] bytes = File.ReadAllBytes(tempFile);
                    BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            asset.SetData(bytes);
                            if (listViewPckAssets.SelectedItems.Count == 1 && listViewPckAssets.SelectedItems[0].Tag == asset)
                                SelectPckAsset(asset);

                            UpdatePckBytes();
                            toolStripStatusLabel1.Text = $"Reloaded edited asset: {asset.Filename}";
                        }
                        catch
                        {
                            // Ignore transient write/read races from external editors.
                        }
                    }));
                }
                catch
                {
                    // Ignore transient file-system events.
                }
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
            try
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
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Unexpected error while opening SWF: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                toolStripStatusLabel1.Text = "Failed to open SWF editor.";
            }
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

        private bool TryFindJava(out string reason)
        {
            reason = string.Empty;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "java",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null)
                {
                    reason = "Failed to start the 'where' command.";
                    return false;
                }

                proc.WaitForExit(2000);
                if (proc.ExitCode != 0)
                {
                    reason = "Java is not found on PATH. Install Java 8+ and ensure 'java' is on your PATH.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private void OpenSwfExternally(string jar, string tempFile, string archiveKey)
        {
            try
            {
                if (!File.Exists(jar))
                {
                    MessageBox.Show(this, $"The SWF editor jar was not found:\n{jar}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (!File.Exists(tempFile))
                {
                    MessageBox.Show(this, $"The temporary SWF file was not found:\n{tempFile}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (!TryFindJava(out var javaReason))
                {
                    MessageBox.Show(this, "Cannot launch SWF editor because Java is not available:\n" + javaReason, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string jarDir = Path.GetDirectoryName(jar) ?? Environment.CurrentDirectory;

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "java",
                    Arguments = $"-jar \"{jar}\" \"{tempFile}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = jarDir
                };

                var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null)
                {
                    MessageBox.Show(this, "Failed to start Java process.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                proc.EnableRaisingEvents = true;
                proc.Exited += (s, e) =>
                {
                    try
                    {
                        string outText = proc.StandardOutput.ReadToEnd();
                        string errText = proc.StandardError.ReadToEnd();
                        if (proc.ExitCode != 0)
                        {
                            BeginInvoke((Action)(() =>
                            {
                                MessageBox.Show(this, $"SWF editor exited with code {proc.ExitCode}.\n\nSTDOUT:\n{outText}\n\nSTDERR:\n{errText}", "SWF Editor Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }));
                        }
                    }
                    catch { }
                };

                toolStripStatusLabel1.Text = $"Opened SWF externally: {archiveKey}.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to launch SWF editor: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task OpenSwfFileAsync(string swfPath, string displayName)
        {
            try
            {
                if (_embedSwfEditor)
                {
                    bool loadedInViewer = await DisplaySwfInInternalViewerAsync(swfPath);
                    if (loadedInViewer)
                        return;

                    var fallbackJar = GetSwfEditorJarPath();
                    if (fallbackJar != null)
                    {
                        bool embedded = await TryEmbedSwfEditorAsync(fallbackJar, swfPath, displayName);
                        if (embedded)
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
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Unexpected error opening SWF editor: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                toolStripStatusLabel1.Text = "Failed to open SWF editor.";
            }
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

        private async Task<bool> TryEmbedSwfEditorAsync(string jar, string tempFile, string archiveKey)
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

                // Let the monitor timer handle embedding when the editor window appears.
                tabControlMain.SelectedTab = tabPageSwfEditor;
                toolStripStatusLabel1.Text = $"Launched SWF editor (embedded window will appear shortly).";
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to launch embedded SWF editor: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
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
