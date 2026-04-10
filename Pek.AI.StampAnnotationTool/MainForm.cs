using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace Pek.AI.StampAnnotationTool;

internal sealed class MainForm : Form
{
    private static readonly String[] SupportedExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".webp", ".tif", ".tiff"];

    private readonly ListBox _imageListBox;
    private readonly AnnotationCanvas _canvas;
    private readonly ComboBox _classComboBox;
    private readonly TextBox _labelsTextBox;
    private readonly Label _statusLabel;
    private readonly Dictionary<String, AnnotationDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<String> _imageFiles = [];

    private String? _currentFolder;
    private Bitmap? _currentBitmap;
    private String? _currentImagePath;
    private List<String> _labels = ["seal", "partial-seal"];

    public MainForm()
    {
        Text = "印章标注工具";
        Width = 1400;
        Height = 900;
        MinimumSize = new Size(1100, 760);
        KeyPreview = true;

        var mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 340,
            FixedPanel = FixedPanel.Panel1
        };
        Controls.Add(mainSplit);

        var leftLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(8)
        };
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainSplit.Panel1.Controls.Add(leftLayout);

        var openButton = new Button { Text = "打开图片目录", Dock = DockStyle.Top, Height = 38 };
        openButton.Click += (_, _) => OpenImageFolder();
        leftLayout.Controls.Add(openButton, 0, 0);

        var labelsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 8, 0, 8)
        };
        labelsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        labelsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        labelsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        labelsPanel.Controls.Add(new Label { Text = "类别列表，逗号分隔，顺序会写入 YOLO 和 ONNX 标签清单", AutoSize = true }, 0, 0);
        _labelsTextBox = new TextBox { Text = String.Join(',', _labels), Dock = DockStyle.Top };
        labelsPanel.Controls.Add(_labelsTextBox, 0, 1);
        var applyLabelsButton = new Button { Text = "应用类别", Dock = DockStyle.Top, Height = 32 };
        applyLabelsButton.Click += (_, _) => ApplyLabels();
        labelsPanel.Controls.Add(applyLabelsButton, 0, 2);
        leftLayout.Controls.Add(labelsPanel, 0, 1);

        _imageListBox = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        _imageListBox.SelectedIndexChanged += (_, _) => LoadSelectedImage();
        leftLayout.Controls.Add(_imageListBox, 0, 2);

        var navigationPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        navigationPanel.Controls.Add(CreateActionButton("上一张", (_, _) => Navigate(-1)));
        navigationPanel.Controls.Add(CreateActionButton("下一张", (_, _) => Navigate(1)));
        navigationPanel.Controls.Add(CreateActionButton("保存当前", (_, _) => SaveCurrentAnnotations()));
        navigationPanel.Controls.Add(CreateActionButton("全部保存", (_, _) => SaveAllAnnotations()));
        leftLayout.Controls.Add(navigationPanel, 0, 3);

        var exportPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        exportPanel.Controls.Add(CreateActionButton("导出 YOLO 数据集", (_, _) => ExportDataset()));
        exportPanel.Controls.Add(CreateActionButton("重新加载标签", (_, _) => ReloadLabelsFromFolder()));
        leftLayout.Controls.Add(exportPanel, 0, 4);

        var rightLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8)
        };
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainSplit.Panel2.Controls.Add(rightLayout);

        var toolPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = true
        };
        toolPanel.Controls.Add(new Label { Text = "当前类别", AutoSize = true, Margin = new Padding(0, 10, 4, 0) });
        _classComboBox = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
        toolPanel.Controls.Add(_classComboBox);
        toolPanel.Controls.Add(new Label { Text = "操作：左键拖拽画框，右键点框删除，Ctrl+S 保存，Delete 删除最后一个框", AutoSize = true, Margin = new Padding(18, 10, 0, 0) });
        rightLayout.Controls.Add(toolPanel, 0, 0);

        _canvas = new AnnotationCanvas { Dock = DockStyle.Fill, BackColor = Color.FromArgb(32, 32, 32) };
        _classComboBox.SelectedIndexChanged += (_, _) => _canvas.ActiveClassId = Math.Max(0, _classComboBox.SelectedIndex);
        _canvas.AnnotationCreated += (_, _) => RefreshImageListDisplay();
        _canvas.AnnotationRemoved += (_, _) => RefreshImageListDisplay();
        rightLayout.Controls.Add(_canvas, 0, 1);

        _statusLabel = new Label { Text = "先打开一个图片目录开始标注。", AutoSize = true, Padding = new Padding(0, 8, 0, 4) };
        rightLayout.Controls.Add(_statusLabel, 0, 2);

        var footer = new Label
        {
            Text = "导出内容包含：每张图对应的 YOLO txt、labels.txt、dataset.yaml、annotations.json。当前 ONNX 推理端可直接读取 labels.txt。",
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 8)
        };
        rightLayout.Controls.Add(footer, 0, 3);

        UpdateClassComboBox();
    }

    protected override Boolean ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.S))
        {
            SaveCurrentAnnotations();
            return true;
        }

        if (keyData == Keys.Delete)
        {
            _canvas.RemoveLastAnnotation();
            RefreshImageListDisplay();
            return true;
        }

        if (TryHandleClassHotkey(keyData))
            return true;

        if (TryHandleSelectedAnnotationHotkey(keyData))
        {
            RefreshImageListDisplay();
            return true;
        }

        if (keyData == Keys.PageDown)
        {
            Navigate(1);
            return true;
        }

        if (keyData == Keys.PageUp)
        {
            Navigate(-1);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private Boolean TryHandleClassHotkey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (key < Keys.D1 || key > Keys.D9)
            return false;

        var classIndex = (Int32)(key - Keys.D1);
        if (classIndex < 0 || classIndex >= _classComboBox.Items.Count)
            return false;

        _classComboBox.SelectedIndex = classIndex;
        _canvas.ApplyClassToSelection(classIndex);
        RefreshImageListDisplay();
        return true;
    }

    private Boolean TryHandleSelectedAnnotationHotkey(Keys keyData)
    {
        var step = keyData.HasFlag(Keys.Shift) ? 0.01f : 0.0025f;
        var key = keyData & Keys.KeyCode;

        return key switch
        {
            Keys.Left when keyData.HasFlag(Keys.Shift) => _canvas.ResizeSelected(-step, 0),
            Keys.Right when keyData.HasFlag(Keys.Shift) => _canvas.ResizeSelected(step, 0),
            Keys.Up when keyData.HasFlag(Keys.Shift) => _canvas.ResizeSelected(0, -step),
            Keys.Down when keyData.HasFlag(Keys.Shift) => _canvas.ResizeSelected(0, step),
            Keys.Left => _canvas.NudgeSelected(-step, 0),
            Keys.Right => _canvas.NudgeSelected(step, 0),
            Keys.Up => _canvas.NudgeSelected(0, -step),
            Keys.Down => _canvas.NudgeSelected(0, step),
            _ => false
        };
    }

    private static Button CreateActionButton(String text, EventHandler handler)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 34, Padding = new Padding(8, 4, 8, 4) };
        button.Click += handler;
        return button;
    }

    private void OpenImageFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择需要标注的图片目录",
            ShowNewFolderButton = false,
            InitialDirectory = _currentFolder ?? AppContext.BaseDirectory
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        LoadFolder(dialog.SelectedPath);
    }

    private void LoadFolder(String folderPath)
    {
        _currentFolder = folderPath;
        _imageFiles.Clear();
        _documents.Clear();
        _imageListBox.Items.Clear();

        var images = Directory.EnumerateFiles(folderPath)
            .Where(file => SupportedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _imageFiles.AddRange(images);
        LoadLabelsFromFolderIfPresent(folderPath);

        foreach (var imageFile in _imageFiles)
        {
            _documents[imageFile] = LoadDocument(imageFile);
            _imageListBox.Items.Add(BuildDisplayName(imageFile));
        }

        if (_imageFiles.Count > 0)
        {
            _imageListBox.SelectedIndex = 0;
            _statusLabel.Text = $"已加载 {_imageFiles.Count} 张图片。";
        }
        else
        {
            ClearCurrentImage();
            _statusLabel.Text = "当前目录没有可标注图片。";
        }
    }

    private void ReloadLabelsFromFolder()
    {
        if (String.IsNullOrWhiteSpace(_currentFolder))
        {
            MessageBox.Show(this, "请先打开图片目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        LoadLabelsFromFolderIfPresent(_currentFolder);
        UpdateClassComboBox();
        _canvas.Labels = _labels;
        _canvas.Invalidate();
        _statusLabel.Text = "已重新加载 labels.txt。";
    }

    private void LoadLabelsFromFolderIfPresent(String folderPath)
    {
        var labelsPath = Path.Combine(folderPath, "labels.txt");
        if (!File.Exists(labelsPath))
            return;

        var labels = File.ReadAllLines(labelsPath, Encoding.UTF8)
            .Select(item => item.Trim())
            .Where(item => !String.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (labels.Count == 0)
            return;

        _labels = labels;
        _labelsTextBox.Text = String.Join(',', _labels);
        UpdateClassComboBox();
    }

    private void ApplyLabels()
    {
        var labels = _labelsTextBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (labels.Count == 0)
        {
            MessageBox.Show(this, "至少保留一个类别。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _labels = labels;
        ClampExistingClassIds();
        UpdateClassComboBox();
        _canvas.Labels = _labels;
        _canvas.Invalidate();
        _statusLabel.Text = "类别列表已更新。";
    }

    private void ClampExistingClassIds()
    {
        foreach (var document in _documents.Values)
        {
            foreach (var annotation in document.Annotations)
            {
                if (annotation.ClassId >= _labels.Count)
                    annotation.ClassId = _labels.Count - 1;
            }
        }
    }

    private void UpdateClassComboBox()
    {
        _classComboBox.Items.Clear();
        foreach (var label in _labels)
        {
            _classComboBox.Items.Add(label);
        }

        if (_classComboBox.Items.Count > 0)
            _classComboBox.SelectedIndex = Math.Min(Math.Max(0, _classComboBox.SelectedIndex), _classComboBox.Items.Count - 1);
    }

    private void LoadSelectedImage()
    {
        if (_imageListBox.SelectedIndex < 0 || _imageListBox.SelectedIndex >= _imageFiles.Count)
        {
            ClearCurrentImage();
            return;
        }

        var imagePath = _imageFiles[_imageListBox.SelectedIndex];
        _currentImagePath = imagePath;
        _currentBitmap?.Dispose();
        _currentBitmap = LoadBitmap(imagePath);
        var document = _documents[imagePath];
        _canvas.CurrentBitmap = _currentBitmap;
        _canvas.CurrentAnnotations = document.Annotations;
        _canvas.Labels = _labels;
        _canvas.ActiveClassId = Math.Max(0, _classComboBox.SelectedIndex);
        _canvas.Invalidate();
        _statusLabel.Text = $"当前图片：{Path.GetFileName(imagePath)}，标注数：{document.Annotations.Count}";
    }

    private void ClearCurrentImage()
    {
        _currentImagePath = null;
        _currentBitmap?.Dispose();
        _currentBitmap = null;
        _canvas.CurrentBitmap = null;
        _canvas.CurrentAnnotations = null;
        _canvas.Invalidate();
    }

    private static Bitmap LoadBitmap(String imagePath)
    {
        using var stream = File.OpenRead(imagePath);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private AnnotationDocument LoadDocument(String imagePath)
    {
        var labelsPath = GetLabelPath(imagePath);
        var document = new AnnotationDocument { ImagePath = imagePath };
        if (!File.Exists(labelsPath))
            return document;

        foreach (var line in File.ReadAllLines(labelsPath, Encoding.UTF8))
        {
            if (String.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 5)
                continue;

            if (!Int32.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var classId))
                continue;

            if (!Single.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var centerX))
                continue;
            if (!Single.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var centerY))
                continue;
            if (!Single.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var width))
                continue;
            if (!Single.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
                continue;

            document.Annotations.Add(new AnnotationBox
            {
                ClassId = Math.Max(0, classId),
                NormalizedBox = new RectangleF(centerX - width / 2f, centerY - height / 2f, width, height)
            });
        }

        return document;
    }

    private void Navigate(Int32 offset)
    {
        if (_imageFiles.Count == 0)
            return;

        var nextIndex = Math.Max(0, Math.Min(_imageFiles.Count - 1, _imageListBox.SelectedIndex + offset));
        _imageListBox.SelectedIndex = nextIndex;
    }

    private void SaveCurrentAnnotations()
    {
        if (String.IsNullOrWhiteSpace(_currentImagePath))
            return;

        WriteAnnotations(_currentImagePath, _documents[_currentImagePath]);
        WriteLabelsManifest(Path.GetDirectoryName(_currentImagePath)!);
        RefreshImageListDisplay();
        _statusLabel.Text = $"已保存：{Path.GetFileName(_currentImagePath)}";
    }

    private void SaveAllAnnotations()
    {
        if (_imageFiles.Count == 0)
            return;

        foreach (var imagePath in _imageFiles)
        {
            WriteAnnotations(imagePath, _documents[imagePath]);
        }

        if (!String.IsNullOrWhiteSpace(_currentFolder))
        {
            WriteLabelsManifest(_currentFolder);
            WriteAnnotationManifest(_currentFolder);
        }

        RefreshImageListDisplay();
        _statusLabel.Text = $"已保存 {_imageFiles.Count} 张图片的标注。";
    }

    private void ExportDataset()
    {
        if (_imageFiles.Count == 0)
        {
            MessageBox.Show(this, "当前没有可导出的图片。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SaveAllAnnotations();

        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 YOLO 数据集导出目录",
            ShowNewFolderButton = true,
            InitialDirectory = _currentFolder ?? AppContext.BaseDirectory
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var outputFolder = dialog.SelectedPath;
        Directory.CreateDirectory(outputFolder);

        var trainImageFolder = Path.Combine(outputFolder, "images", "train");
        var valImageFolder = Path.Combine(outputFolder, "images", "val");
        var trainLabelFolder = Path.Combine(outputFolder, "labels", "train");
        var valLabelFolder = Path.Combine(outputFolder, "labels", "val");
        Directory.CreateDirectory(trainImageFolder);
        Directory.CreateDirectory(valImageFolder);
        Directory.CreateDirectory(trainLabelFolder);
        Directory.CreateDirectory(valLabelFolder);

        var labeledImages = _imageFiles
            .Where(path => _documents.TryGetValue(path, out var document) && document.Annotations.Count > 0)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var index = 0; index < labeledImages.Length; index++)
        {
            var imagePath = labeledImages[index];
            var subset = index % 5 == 0 ? "val" : "train";
            var targetImageFolder = subset == "val" ? valImageFolder : trainImageFolder;
            var targetLabelFolder = subset == "val" ? valLabelFolder : trainLabelFolder;
            var fileName = Path.GetFileName(imagePath);
            File.Copy(imagePath, Path.Combine(targetImageFolder, fileName), overwrite: true);
            File.Copy(GetLabelPath(imagePath), Path.Combine(targetLabelFolder, Path.ChangeExtension(fileName, ".txt")), overwrite: true);
        }

        File.WriteAllLines(Path.Combine(outputFolder, "labels.txt"), _labels, Encoding.UTF8);
        File.WriteAllText(Path.Combine(outputFolder, "dataset.yaml"), BuildDatasetYaml(outputFolder), Encoding.UTF8);
        File.WriteAllText(Path.Combine(outputFolder, "annotations.json"), JsonSerializer.Serialize(BuildManifestItems(), new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

        _statusLabel.Text = $"已导出数据集：{outputFolder}";
        MessageBox.Show(this, $"已导出 {labeledImages.Length} 张已标注图片。", "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private String BuildDatasetYaml(String outputFolder)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"path: {outputFolder.Replace('\\', '/')}");
        builder.AppendLine("train: images/train");
        builder.AppendLine("val: images/val");
        builder.AppendLine();
        builder.AppendLine("names:");

        for (var index = 0; index < _labels.Count; index++)
        {
            builder.AppendLine($"  {index}: {_labels[index]}");
        }

        return builder.ToString();
    }

    private IReadOnlyList<AnnotationManifestItem> BuildManifestItems()
    {
        return _imageFiles.Select(path => new AnnotationManifestItem
        {
            ImageFile = Path.GetFileName(path),
            AnnotationFile = Path.GetFileName(GetLabelPath(path)),
            Count = _documents[path].Annotations.Count
        }).ToArray();
    }

    private void WriteAnnotations(String imagePath, AnnotationDocument document)
    {
        var labelPath = GetLabelPath(imagePath);
        var lines = document.Annotations.Select(annotation => FormatAnnotation(annotation)).ToArray();
        File.WriteAllLines(labelPath, lines, Encoding.UTF8);
    }

    private void WriteLabelsManifest(String folderPath)
    {
        File.WriteAllLines(Path.Combine(folderPath, "labels.txt"), _labels, Encoding.UTF8);
    }

    private void WriteAnnotationManifest(String folderPath)
    {
        var json = JsonSerializer.Serialize(BuildManifestItems(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(folderPath, "annotations.json"), json, Encoding.UTF8);
    }

    private static String FormatAnnotation(AnnotationBox annotation)
    {
        var centerX = annotation.NormalizedBox.X + annotation.NormalizedBox.Width / 2f;
        var centerY = annotation.NormalizedBox.Y + annotation.NormalizedBox.Height / 2f;
        return String.Join(' ',
            annotation.ClassId.ToString(CultureInfo.InvariantCulture),
            centerX.ToString("0.######", CultureInfo.InvariantCulture),
            centerY.ToString("0.######", CultureInfo.InvariantCulture),
            annotation.NormalizedBox.Width.ToString("0.######", CultureInfo.InvariantCulture),
            annotation.NormalizedBox.Height.ToString("0.######", CultureInfo.InvariantCulture));
    }

    private static String GetLabelPath(String imagePath) => Path.ChangeExtension(imagePath, ".txt");

    private void RefreshImageListDisplay()
    {
        var selectedIndex = _imageListBox.SelectedIndex;
        _imageListBox.BeginUpdate();
        _imageListBox.Items.Clear();
        foreach (var imagePath in _imageFiles)
        {
            _imageListBox.Items.Add(BuildDisplayName(imagePath));
        }
        _imageListBox.EndUpdate();

        if (selectedIndex >= 0 && selectedIndex < _imageListBox.Items.Count)
            _imageListBox.SelectedIndex = selectedIndex;

        if (!String.IsNullOrWhiteSpace(_currentImagePath))
            _statusLabel.Text = $"当前图片：{Path.GetFileName(_currentImagePath)}，标注数：{_documents[_currentImagePath].Annotations.Count}";
    }

    private String BuildDisplayName(String imagePath)
    {
        var count = _documents.TryGetValue(imagePath, out var document) ? document.Annotations.Count : 0;
        return $"{Path.GetFileName(imagePath)}    [{count}]";
    }
}

internal sealed class AnnotationCanvas : Control
{
    private Bitmap? _currentBitmap;
    private List<AnnotationBox>? _annotations;
    private IReadOnlyList<String> _labels = [];
    private RectangleF _imageBounds;
    private Boolean _isDrawing;
    private Boolean _isDraggingSelection;
    private Point _dragStart;
    private Point _dragEnd;
    private Point _lastDragPoint;
    private Int32 _selectedIndex = -1;

    public AnnotationCanvas()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Bitmap? CurrentBitmap
    {
        get => _currentBitmap;
        set
        {
            _currentBitmap = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public List<AnnotationBox>? CurrentAnnotations
    {
        get => _annotations;
        set
        {
            _annotations = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<String> Labels
    {
        get => _labels;
        set
        {
            _labels = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Int32 ActiveClassId { get; set; }

    public event EventHandler? AnnotationCreated;

    public event EventHandler? AnnotationRemoved;

    public void RemoveLastAnnotation()
    {
        if (_annotations == null || _annotations.Count == 0)
            return;

        _annotations.RemoveAt(_annotations.Count - 1);
        _selectedIndex = Math.Min(_selectedIndex, _annotations.Count - 1);
        AnnotationRemoved?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public Boolean ApplyClassToSelection(Int32 classId)
    {
        if (!HasValidSelection())
            return false;

        _annotations![_selectedIndex].ClassId = Math.Max(0, classId);
        Invalidate();
        return true;
    }

    public Boolean NudgeSelected(Single deltaX, Single deltaY)
    {
        if (!HasValidSelection())
            return false;

        var box = _annotations![_selectedIndex].NormalizedBox;
        box.X = Math.Clamp(box.X + deltaX, 0f, 1f - box.Width);
        box.Y = Math.Clamp(box.Y + deltaY, 0f, 1f - box.Height);
        _annotations[_selectedIndex].NormalizedBox = box;
        Invalidate();
        return true;
    }

    public Boolean ResizeSelected(Single deltaWidth, Single deltaHeight)
    {
        if (!HasValidSelection())
            return false;

        var box = _annotations![_selectedIndex].NormalizedBox;
        box.Width = Math.Clamp(box.Width + deltaWidth, 0.005f, 1f - box.X);
        box.Height = Math.Clamp(box.Height + deltaHeight, 0.005f, 1f - box.Y);
        _annotations[_selectedIndex].NormalizedBox = box;
        Invalidate();
        return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);

        if (_currentBitmap == null)
        {
            using var brush = new SolidBrush(Color.Gainsboro);
            using var font = new Font(Font.FontFamily, 16, FontStyle.Bold);
            var text = "请先在左侧打开图片目录";
            var size = e.Graphics.MeasureString(text, font);
            e.Graphics.DrawString(text, font, brush, (Width - size.Width) / 2f, (Height - size.Height) / 2f);
            return;
        }

        _imageBounds = CalculateImageBounds(_currentBitmap.Size, ClientRectangle);
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(_currentBitmap, _imageBounds);

        if (_annotations != null)
        {
            for (var index = 0; index < _annotations.Count; index++)
            {
                DrawAnnotation(e.Graphics, _annotations[index], highlight: index == _selectedIndex);
            }
        }

        if (_isDrawing)
        {
            var rect = NormalizeDisplayRectangle(_dragStart, _dragEnd);
            using var pen = new Pen(Color.DeepSkyBlue, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            e.Graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_currentBitmap == null || _annotations == null)
            return;

        if (e.Button == MouseButtons.Left)
        {
            if (!_imageBounds.Contains(e.Location))
            {
                _selectedIndex = -1;
                Invalidate();
                return;
            }

            var selectedIndex = HitTestAnnotation(e.Location);
            if (selectedIndex >= 0)
            {
                _selectedIndex = selectedIndex;
                _isDraggingSelection = true;
                _lastDragPoint = e.Location;
                Capture = true;
                Invalidate();
                return;
            }

            _isDrawing = true;
            _selectedIndex = -1;
            _dragStart = e.Location;
            _dragEnd = e.Location;
            Capture = true;
            Invalidate();
        }
        else if (e.Button == MouseButtons.Right)
        {
            RemoveAnnotationAt(e.Location);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_isDraggingSelection)
        {
            DragSelectedAnnotation(e.Location);
            return;
        }

        if (!_isDrawing)
            return;

        _dragEnd = e.Location;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_isDraggingSelection)
        {
            _isDraggingSelection = false;
            Capture = false;
            return;
        }

        if (!_isDrawing || _currentBitmap == null || _annotations == null)
            return;

        _isDrawing = false;
        Capture = false;
        _dragEnd = e.Location;

        var displayRect = NormalizeDisplayRectangle(_dragStart, _dragEnd);
        if (displayRect.Width < 8 || displayRect.Height < 8)
        {
            Invalidate();
            return;
        }

        var normalized = ConvertDisplayToNormalized(displayRect);
        if (normalized.Width < 0.005f || normalized.Height < 0.005f)
        {
            Invalidate();
            return;
        }

        _annotations.Add(new AnnotationBox
        {
            ClassId = Math.Max(0, ActiveClassId),
            NormalizedBox = normalized
        });
        _selectedIndex = _annotations.Count - 1;
        AnnotationCreated?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void DragSelectedAnnotation(Point point)
    {
        if (!HasValidSelection())
            return;

        var deltaX = (point.X - _lastDragPoint.X) / Math.Max(1f, _imageBounds.Width);
        var deltaY = (point.Y - _lastDragPoint.Y) / Math.Max(1f, _imageBounds.Height);
        if (Math.Abs(deltaX) < Single.Epsilon && Math.Abs(deltaY) < Single.Epsilon)
            return;

        var box = _annotations![_selectedIndex].NormalizedBox;
        box.X = Math.Clamp(box.X + deltaX, 0f, 1f - box.Width);
        box.Y = Math.Clamp(box.Y + deltaY, 0f, 1f - box.Height);
        _annotations[_selectedIndex].NormalizedBox = box;
        _lastDragPoint = point;
        Invalidate();
    }

    private void RemoveAnnotationAt(Point point)
    {
        if (_annotations == null)
            return;

        for (var index = _annotations.Count - 1; index >= 0; index--)
        {
            var rect = ConvertNormalizedToDisplay(_annotations[index].NormalizedBox);
            if (!rect.Contains(point))
                continue;

            _annotations.RemoveAt(index);
            _selectedIndex = _annotations.Count == 0 ? -1 : Math.Min(_selectedIndex, _annotations.Count - 1);
            AnnotationRemoved?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return;
        }

        _selectedIndex = -1;
        Invalidate();
    }

    private Int32 HitTestAnnotation(Point point)
    {
        if (_annotations == null)
            return -1;

        for (var index = _annotations.Count - 1; index >= 0; index--)
        {
            if (ConvertNormalizedToDisplay(_annotations[index].NormalizedBox).Contains(point))
                return index;
        }

        return -1;
    }

    private Boolean HasValidSelection() => _annotations != null && _selectedIndex >= 0 && _selectedIndex < _annotations.Count;

    private void DrawAnnotation(Graphics graphics, AnnotationBox annotation, Boolean highlight)
    {
        var rect = ConvertNormalizedToDisplay(annotation.NormalizedBox);
        var color = highlight ? Color.Gold : PickColor(annotation.ClassId);
        using var pen = new Pen(color, 2);
        using var backgroundBrush = new SolidBrush(Color.FromArgb(196, color));
        using var textBrush = new SolidBrush(Color.Black);
        graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);

        var label = annotation.ClassId >= 0 && annotation.ClassId < _labels.Count ? _labels[annotation.ClassId] : $"class_{annotation.ClassId}";
        using var font = new Font(Font.FontFamily, 9, FontStyle.Bold);
        var textSize = graphics.MeasureString(label, font);
        var textRect = new RectangleF(rect.X, Math.Max(_imageBounds.Top, rect.Y - textSize.Height), textSize.Width + 10, textSize.Height + 4);
        graphics.FillRectangle(backgroundBrush, textRect);
        graphics.DrawString(label, font, textBrush, textRect.X + 5, textRect.Y + 2);
    }

    private RectangleF ConvertNormalizedToDisplay(RectangleF normalized)
    {
        return new RectangleF(
            _imageBounds.Left + normalized.X * _imageBounds.Width,
            _imageBounds.Top + normalized.Y * _imageBounds.Height,
            normalized.Width * _imageBounds.Width,
            normalized.Height * _imageBounds.Height);
    }

    private RectangleF ConvertDisplayToNormalized(RectangleF display)
    {
        var left = Math.Clamp((display.Left - _imageBounds.Left) / _imageBounds.Width, 0f, 1f);
        var top = Math.Clamp((display.Top - _imageBounds.Top) / _imageBounds.Height, 0f, 1f);
        var right = Math.Clamp((display.Right - _imageBounds.Left) / _imageBounds.Width, 0f, 1f);
        var bottom = Math.Clamp((display.Bottom - _imageBounds.Top) / _imageBounds.Height, 0f, 1f);
        return RectangleF.FromLTRB(left, top, right, bottom);
    }

    private static RectangleF NormalizeDisplayRectangle(Point first, Point second)
    {
        var left = Math.Min(first.X, second.X);
        var top = Math.Min(first.Y, second.Y);
        var right = Math.Max(first.X, second.X);
        var bottom = Math.Max(first.Y, second.Y);
        return RectangleF.FromLTRB(left, top, right, bottom);
    }

    private static RectangleF CalculateImageBounds(Size imageSize, Rectangle clientRectangle)
    {
        const Int32 padding = 12;
        var availableWidth = Math.Max(1, clientRectangle.Width - padding * 2);
        var availableHeight = Math.Max(1, clientRectangle.Height - padding * 2);
        var scale = Math.Min(availableWidth / (Single)imageSize.Width, availableHeight / (Single)imageSize.Height);
        var width = imageSize.Width * scale;
        var height = imageSize.Height * scale;
        var x = clientRectangle.Left + (clientRectangle.Width - width) / 2f;
        var y = clientRectangle.Top + (clientRectangle.Height - height) / 2f;
        return new RectangleF(x, y, width, height);
    }

    private static Color PickColor(Int32 classId)
    {
        Color[] palette = [Color.Orange, Color.LimeGreen, Color.DeepSkyBlue, Color.HotPink, Color.Gold, Color.Cyan];
        return palette[Math.Abs(classId) % palette.Length];
    }
}

internal sealed class AnnotationDocument
{
    public required String ImagePath { get; init; }

    public List<AnnotationBox> Annotations { get; } = [];
}

internal sealed class AnnotationBox
{
    public Int32 ClassId { get; set; }

    public RectangleF NormalizedBox { get; set; }
}

internal sealed class AnnotationManifestItem
{
    public required String ImageFile { get; init; }

    public required String AnnotationFile { get; init; }

    public required Int32 Count { get; init; }
}