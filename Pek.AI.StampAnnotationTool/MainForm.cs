using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Cv2 = OpenCvSharp.Cv2;
using CvAdaptiveThresholdTypes = OpenCvSharp.AdaptiveThresholdTypes;
using CvColorConversionCodes = OpenCvSharp.ColorConversionCodes;
using CvContourApproximationModes = OpenCvSharp.ContourApproximationModes;
using CvImreadModes = OpenCvSharp.ImreadModes;
using CvMat = OpenCvSharp.Mat;
using CvMatType = OpenCvSharp.MatType;
using CvMorphShapes = OpenCvSharp.MorphShapes;
using CvMorphTypes = OpenCvSharp.MorphTypes;
using CvRect = OpenCvSharp.Rect;
using CvRetrievalModes = OpenCvSharp.RetrievalModes;
using CvScalar = OpenCvSharp.Scalar;
using CvSize = OpenCvSharp.Size;
using CvThresholdTypes = OpenCvSharp.ThresholdTypes;

namespace Pek.AI.StampAnnotationTool;

internal sealed class MainForm : Form
{
    private static readonly String[] SupportedExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".webp", ".tif", ".tiff"];

    private readonly ListBox _imageListBox;
    private readonly SplitContainer _mainSplit;
    private readonly AnnotationCanvas _canvas;
    private readonly ComboBox _classComboBox;
    private readonly CheckBox _autoSaveCheckBox;
    private readonly CheckBox _openOnnxAfterTrainCheckBox;
    private readonly TextBox _modelPathTextBox;
    private readonly TextBox _labelsTextBox;
    private readonly TextBox _trainBaseModelTextBox;
    private readonly TextBox _trainEpochsTextBox;
    private readonly TextBox _trainBatchTextBox;
    private readonly TextBox _trainImageSizeTextBox;
    private readonly TextBox _trainDeviceTextBox;
    private readonly Button _trainButton;
    private readonly TextBox _trainLogTextBox;
    private readonly Label _statusLabel;
    private readonly StampPreAnnotationDetector _preAnnotationDetector = new();
    private readonly Dictionary<String, AnnotationDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<String> _imageFiles = [];

    private String? _currentFolder;
    private Bitmap? _currentBitmap;
    private String? _currentImagePath;
    private List<String> _labels = ["seal", "partial-seal"];
    private Boolean _suppressImageSelectionChanged;
    private Boolean _isTraining;
    private Boolean _initialLeftPanelApplied;

    public MainForm()
    {
        Text = "印章标注工具";
        Width = 1400;
        Height = 900;
        MinimumSize = new Size(1100, 760);
        KeyPreview = true;

        _mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 560,
            Panel1MinSize = 520,
            FixedPanel = FixedPanel.Panel1
        };
        Controls.Add(_mainSplit);
        Shown += (_, _) => EnsureInitialLeftPanelWidth();

        var leftLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(8)
        };
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _mainSplit.Panel1.Controls.Add(leftLayout);

        var openButton = new Button { Text = "打开图片目录", Dock = DockStyle.Top, Height = 38, AutoSize = false, Width = 180 };
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
        _labelsTextBox = new TextBox { Text = String.Join(',', _labels), Dock = DockStyle.Top, Width = 380 };
        labelsPanel.Controls.Add(_labelsTextBox, 0, 1);
        var applyLabelsButton = new Button { Text = "应用类别", Dock = DockStyle.Top, Height = 32, AutoSize = false, Width = 180 };
        applyLabelsButton.Click += (_, _) => ApplyLabels();
        labelsPanel.Controls.Add(applyLabelsButton, 0, 2);
        var modelPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0)
        };
        modelPanel.Controls.Add(new Label { Text = "预标注模型", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _modelPathTextBox = new TextBox { Width = 180 };
        modelPanel.Controls.Add(_modelPathTextBox);
        var browseModelButton = new Button { Text = "选择模型", AutoSize = true, Height = 30 };
        browseModelButton.Click += (_, _) => SelectOnnxModel();
        modelPanel.Controls.Add(browseModelButton);
        labelsPanel.Controls.Add(modelPanel, 0, 3);
        leftLayout.Controls.Add(labelsPanel, 0, 1);

        _imageListBox = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
        _imageListBox.SelectedIndexChanged += (_, _) => OnSelectedImageChanged();
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
        _autoSaveCheckBox = new CheckBox { Text = "自动保存", AutoSize = true, Checked = true, Margin = new Padding(8, 8, 0, 0) };
        navigationPanel.Controls.Add(_autoSaveCheckBox);
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
        exportPanel.Controls.Add(CreateActionButton("导出轻量标签", (_, _) => ExportLabelBundle()));
        exportPanel.Controls.Add(CreateActionButton("自动预标注当前", (_, _) => AutoAnnotateCurrent()));
        exportPanel.Controls.Add(CreateActionButton("自动预标注未标注", (_, _) => AutoAnnotateUnlabeled()));
        exportPanel.Controls.Add(CreateActionButton("重置视图", (_, _) => ResetCanvasView()));
        exportPanel.Controls.Add(CreateActionButton("重新加载标签", (_, _) => ReloadLabelsFromFolder()));
        leftLayout.Controls.Add(exportPanel, 0, 4);

        var trainGroup = new GroupBox
        {
            Text = "训练并导出模型",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10)
        };
        var trainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 6,
            AutoSize = true
        };
        trainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        trainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        trainLayout.Controls.Add(new Label { Text = "基础模型", AutoSize = true, Margin = new Padding(0, 8, 8, 0) }, 0, 0);
        _trainBaseModelTextBox = new TextBox { Text = "yolov8n.pt", Dock = DockStyle.Top, Width = 240 };
        trainLayout.Controls.Add(_trainBaseModelTextBox, 1, 0);
        trainLayout.Controls.Add(new Label { Text = "Epochs/Batch", AutoSize = true, Margin = new Padding(0, 8, 8, 0) }, 0, 1);
        var epochsBatchPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Dock = DockStyle.Top };
        _trainEpochsTextBox = new TextBox { Text = "100", Width = 70 };
        _trainBatchTextBox = new TextBox { Text = "16", Width = 70 };
        epochsBatchPanel.Controls.Add(_trainEpochsTextBox);
        epochsBatchPanel.Controls.Add(new Label { Text = "/", AutoSize = true, Margin = new Padding(4, 8, 4, 0) });
        epochsBatchPanel.Controls.Add(_trainBatchTextBox);
        trainLayout.Controls.Add(epochsBatchPanel, 1, 1);
        trainLayout.Controls.Add(new Label { Text = "图像尺寸/设备", AutoSize = true, Margin = new Padding(0, 8, 8, 0) }, 0, 2);
        var imageDevicePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Dock = DockStyle.Top };
        _trainImageSizeTextBox = new TextBox { Text = "640", Width = 70 };
        _trainDeviceTextBox = new TextBox { Text = "cpu", Width = 90 };
        imageDevicePanel.Controls.Add(_trainImageSizeTextBox);
        imageDevicePanel.Controls.Add(new Label { Text = "/", AutoSize = true, Margin = new Padding(4, 8, 4, 0) });
        imageDevicePanel.Controls.Add(_trainDeviceTextBox);
        trainLayout.Controls.Add(imageDevicePanel, 1, 2);
        _openOnnxAfterTrainCheckBox = new CheckBox { Text = "训练完成后自动填入 best.onnx", AutoSize = true, Checked = true, Margin = new Padding(0, 8, 0, 0) };
        trainLayout.Controls.Add(_openOnnxAfterTrainCheckBox, 1, 3);
        _trainButton = new Button { Text = "训练并导出 ONNX", AutoSize = false, Width = 180, Height = 34, Margin = new Padding(0, 8, 0, 0) };
        _trainButton.Click += async (_, _) => await TrainAndExportOnnxAsync();
        trainLayout.Controls.Add(_trainButton, 1, 4);
        trainLayout.Controls.Add(new Label { Text = "说明：会先导出当前目录下的 yolo 数据集，再调用仓库内 train_yolo.py 训练并导出 ONNX。", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 1, 5);
        trainGroup.Controls.Add(trainLayout);
        leftLayout.Controls.Add(trainGroup, 0, 5);

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
        _mainSplit.Panel2.Controls.Add(rightLayout);

        var toolPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = true
        };
        toolPanel.Controls.Add(new Label { Text = "当前类别", AutoSize = true, Margin = new Padding(0, 10, 4, 0) });
        _classComboBox = new ComboBox { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
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

        _trainLogTextBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Height = 180,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(24, 24, 24),
            ForeColor = Color.Gainsboro
        };
        rightLayout.Controls.Add(_trainLogTextBox, 0, 3);

        var footer = new Label
        {
            Text = "导出内容包含：每张图对应的 YOLO txt、labels.txt、dataset.yaml、annotations.json。当前 ONNX 推理端可直接读取 labels.txt。",
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 8)
        };
        rightLayout.Controls.Add(footer, 0, 4);

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
        var button = new Button { Text = text, AutoSize = false, Width = 132, Height = 34, Padding = new Padding(8, 4, 8, 4) };
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
        _suppressImageSelectionChanged = true;
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

        _suppressImageSelectionChanged = false;
        if (_imageFiles.Count > 0)
            LoadSelectedImage();
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

    private void OnSelectedImageChanged()
    {
        if (_suppressImageSelectionChanged)
            return;

        if (_autoSaveCheckBox.Checked)
            SaveCurrentAnnotations();

        LoadSelectedImage();
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
        _canvas.ResetView();
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
        var labeledCount = ExportDatasetToFolder(outputFolder);

        _statusLabel.Text = $"已导出数据集：{outputFolder}";
        MessageBox.Show(this, $"已导出 {labeledCount} 张已标注图片。", "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private Int32 ExportDatasetToFolder(String outputFolder)
    {
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
        return labeledImages.Length;
    }

    private void ExportLabelBundle()
    {
        if (_imageFiles.Count == 0)
        {
            MessageBox.Show(this, "当前没有可导出的图片。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SaveAllAnnotations();

        using var dialog = new FolderBrowserDialog
        {
            Description = "选择轻量标签导出目录",
            ShowNewFolderButton = true,
            InitialDirectory = _currentFolder ?? AppContext.BaseDirectory
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var outputFolder = dialog.SelectedPath;
        var labelsFolder = Path.Combine(outputFolder, "labels");
        Directory.CreateDirectory(labelsFolder);

        var labeledImages = _imageFiles
            .Where(path => _documents.TryGetValue(path, out var document) && document.Annotations.Count > 0)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var imagePath in labeledImages)
        {
            var sourceLabelPath = GetLabelPath(imagePath);
            if (!File.Exists(sourceLabelPath))
                continue;

            var targetLabelPath = Path.Combine(labelsFolder, Path.GetFileName(sourceLabelPath));
            File.Copy(sourceLabelPath, targetLabelPath, overwrite: true);
        }

        File.WriteAllLines(Path.Combine(outputFolder, "labels.txt"), _labels, Encoding.UTF8);
        File.WriteAllText(Path.Combine(outputFolder, "annotations.json"), JsonSerializer.Serialize(BuildManifestItems(), new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

        _statusLabel.Text = $"已导出轻量标签：{outputFolder}";
        MessageBox.Show(this, $"已导出 {labeledImages.Length} 张图片对应的标签文件。", "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void AutoAnnotateCurrent()
    {
        if (String.IsNullOrWhiteSpace(_currentImagePath))
            return;

        var document = _documents[_currentImagePath];
        if (document.Annotations.Count > 0)
        {
            var answer = MessageBox.Show(this, "当前图片已经有标注，是否用自动预标注结果覆盖？", "自动预标注", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
                return;
        }

        ApplyAutoAnnotations(_currentImagePath, document, overwrite: true);
        RefreshImageListDisplay();
        _canvas.Invalidate();
    }

    private void AutoAnnotateUnlabeled()
    {
        if (_imageFiles.Count == 0)
            return;

        var processed = 0;
        foreach (var imagePath in _imageFiles)
        {
            var document = _documents[imagePath];
            if (document.Annotations.Count > 0)
                continue;

            processed += ApplyAutoAnnotations(imagePath, document, overwrite: false);
        }

        RefreshImageListDisplay();
        _canvas.Invalidate();
        _statusLabel.Text = $"自动预标注完成，共生成 {processed} 个候选框。";
    }

    private Int32 ApplyAutoAnnotations(String imagePath, AnnotationDocument document, Boolean overwrite)
    {
        var detections = _preAnnotationDetector.Detect(imagePath);
        detections = DetectWithPreferredPreAnnotation(imagePath, detections);
        if (overwrite)
            document.Annotations.Clear();

        foreach (var detection in detections)
        {
            document.Annotations.Add(new AnnotationBox
            {
                ClassId = ResolveClassId(detection.Label),
                NormalizedBox = detection.NormalizedBox
            });
        }

        if (String.Equals(imagePath, _currentImagePath, StringComparison.OrdinalIgnoreCase))
        {
            _canvas.CurrentAnnotations = document.Annotations;
            _statusLabel.Text = $"自动预标注完成：{Path.GetFileName(imagePath)}，候选数：{detections.Count}";
        }

        return detections.Count;
    }

    private IReadOnlyList<DetectedAnnotation> DetectWithPreferredPreAnnotation(String imagePath, IReadOnlyList<DetectedAnnotation> fallbackDetections)
    {
        var modelPath = _modelPathTextBox.Text.Trim();
        if (String.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            return fallbackDetections;

        try
        {
            using var detector = new OnnxPreAnnotationDetector(modelPath, _labels);
            var modelDetections = detector.Detect(imagePath);
            if (modelDetections.Count > 0)
            {
                _statusLabel.Text = $"已使用训练模型预标注：{Path.GetFileName(imagePath)}，候选数：{modelDetections.Count}";
                return modelDetections;
            }

            _statusLabel.Text = $"模型未检测到候选，已回退启发式预标注：{Path.GetFileName(imagePath)}";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"模型预标注失败，已回退启发式预标注：{ex.Message}";
        }

        return fallbackDetections;
    }

    private void SelectOnnxModel()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 ONNX 预标注模型",
            Filter = "ONNX 模型 (*.onnx)|*.onnx|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = !String.IsNullOrWhiteSpace(_currentFolder) ? _currentFolder : AppContext.BaseDirectory
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _modelPathTextBox.Text = dialog.FileName;
        _statusLabel.Text = $"已加载预标注模型：{Path.GetFileName(dialog.FileName)}";
    }

    private Int32 ResolveClassId(String label)
    {
        var index = _labels.FindIndex(item => String.Equals(item, label, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : 0;
    }

    private void ResetCanvasView()
    {
        _canvas.ResetView();
    }

    private async Task TrainAndExportOnnxAsync()
    {
        if (_isTraining)
            return;

        if (String.IsNullOrWhiteSpace(_currentFolder))
        {
            MessageBox.Show(this, "请先打开并标注一个图片目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SaveAllAnnotations();

        if (!Int32.TryParse(_trainEpochsTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var epochs) || epochs <= 0)
        {
            MessageBox.Show(this, "Epochs 必须是正整数。", "参数错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!Int32.TryParse(_trainBatchTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var batch) || batch <= 0)
        {
            MessageBox.Show(this, "Batch 必须是正整数。", "参数错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!Int32.TryParse(_trainImageSizeTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var imageSize) || imageSize <= 0)
        {
            MessageBox.Show(this, "图像尺寸必须是正整数。", "参数错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var baseModel = _trainBaseModelTextBox.Text.Trim();
        if (String.IsNullOrWhiteSpace(baseModel))
        {
            MessageBox.Show(this, "基础模型不能为空，例如 yolov8n.pt。", "参数错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var device = _trainDeviceTextBox.Text.Trim();
        if (String.IsNullOrWhiteSpace(device))
            device = "cpu";

        var datasetFolder = Path.Combine(_currentFolder, "yolo");
        var runRoot = Path.Combine(_currentFolder, "training-runs");
        var runName = $"stamp_{DateTime.Now:yyyyMMdd_HHmmss}";
        var datasetCount = ExportDatasetToFolder(datasetFolder);
        if (datasetCount == 0)
        {
            MessageBox.Show(this, "当前没有已标注图片，无法训练。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var trainingScriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Pek.AI.StampDetectionDemo", "Training", "train_yolo.py"));
        if (!File.Exists(trainingScriptPath))
        {
            MessageBox.Show(this, $"未找到训练脚本：{trainingScriptPath}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _isTraining = true;
        SetTrainingControlsEnabled(false);
        ClearTrainingLog();
        AppendTrainingLog($"[{DateTime.Now:HH:mm:ss}] 开始训练");
        AppendTrainingLog($"数据集目录: {datasetFolder}");
        AppendTrainingLog($"运行目录: {runRoot}");
        AppendTrainingLog($"运行名称: {runName}");
        _statusLabel.Text = $"开始训练：数据集 {datasetFolder}，运行名 {runName}";

        try
        {
            var processResult = await RunTrainingProcessAsync(trainingScriptPath, datasetFolder, runRoot, runName, baseModel, imageSize, epochs, batch, device, AppendTrainingLog);
            if (processResult.ExitCode != 0)
            {
                AppendTrainingLog($"[{DateTime.Now:HH:mm:ss}] 训练失败，退出码: {processResult.ExitCode}");
                MessageBox.Show(this, processResult.Output, "训练失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _statusLabel.Text = "训练失败，请查看错误输出。";
                return;
            }

            var onnxPath = Path.Combine(runRoot, runName, "weights", "best.onnx");
            if (_openOnnxAfterTrainCheckBox.Checked && File.Exists(onnxPath))
            {
                _modelPathTextBox.Text = onnxPath;
                _statusLabel.Text = $"训练完成，已导出 ONNX：{onnxPath}";
                AppendTrainingLog($"[{DateTime.Now:HH:mm:ss}] 训练完成，已生成: {onnxPath}");
            }
            else
            {
                _statusLabel.Text = $"训练完成：{Path.Combine(runRoot, runName)}";
                AppendTrainingLog($"[{DateTime.Now:HH:mm:ss}] 训练完成，输出目录: {Path.Combine(runRoot, runName)}");
            }

            MessageBox.Show(this, processResult.Output, "训练完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "训练失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = $"训练失败：{ex.Message}";
        }
        finally
        {
            _isTraining = false;
            SetTrainingControlsEnabled(true);
        }
    }

    private void SetTrainingControlsEnabled(Boolean enabled)
    {
        _trainBaseModelTextBox.Enabled = enabled;
        _trainEpochsTextBox.Enabled = enabled;
        _trainBatchTextBox.Enabled = enabled;
        _trainImageSizeTextBox.Enabled = enabled;
        _trainDeviceTextBox.Enabled = enabled;
        _openOnnxAfterTrainCheckBox.Enabled = enabled;
        _trainButton.Enabled = enabled;
        _trainButton.Text = enabled ? "训练并导出 ONNX" : "训练中...";
    }

    private void ClearTrainingLog()
    {
        _trainLogTextBox.Clear();
    }

    private void AppendTrainingLog(String message)
    {
        if (String.IsNullOrWhiteSpace(message))
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action<String>(AppendTrainingLog), message);
            return;
        }

        _trainLogTextBox.AppendText(message + Environment.NewLine);
        _trainLogTextBox.SelectionStart = _trainLogTextBox.TextLength;
        _trainLogTextBox.ScrollToCaret();
        _statusLabel.Text = message;
    }

    private void EnsureInitialLeftPanelWidth()
    {
        if (_initialLeftPanelApplied)
            return;

        _initialLeftPanelApplied = true;
        BeginInvoke(new Action(() =>
        {
            if (Width >= 1200)
                _mainSplit.SplitterDistance = 560;
            else if (Width >= 1050)
                _mainSplit.SplitterDistance = 500;
        }));
    }

    private static async Task<TrainingProcessResult> RunTrainingProcessAsync(String trainingScriptPath, String datasetFolder, String runRoot, String runName, String baseModel, Int32 imageSize, Int32 epochs, Int32 batch, String device, Action<String>? logCallback)
    {
        var pythonLauncher = ResolvePythonLauncher();
        var datasetYamlPath = Path.Combine(datasetFolder, "dataset.yaml");
        var arguments = BuildTrainingArguments(pythonLauncher.ArgumentPrefix, trainingScriptPath, datasetYamlPath, runRoot, runName, baseModel, imageSize, epochs, batch, device);
        logCallback?.Invoke($"使用解释器: {pythonLauncher.DisplayName}");
        logCallback?.Invoke($"基础模型: {baseModel}");

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonLauncher.FileName,
            Arguments = arguments,
            WorkingDirectory = datasetFolder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var outputBuilder = new StringBuilder();
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, args) =>
        {
            if (!String.IsNullOrWhiteSpace(args.Data))
            {
                logCallback?.Invoke(args.Data);
                outputBuilder.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!String.IsNullOrWhiteSpace(args.Data))
            {
                logCallback?.Invoke(args.Data);
                outputBuilder.AppendLine(args.Data);
            }
        };

        if (!process.Start())
            throw new InvalidOperationException("训练进程启动失败。请确认已安装 Python。\n建议先执行：pip install ultralytics");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync().ConfigureAwait(true);

        return new TrainingProcessResult(process.ExitCode, outputBuilder.ToString());
    }

    private static TrainingPythonLauncher ResolvePythonLauncher()
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[]
            {
                new TrainingPythonLauncher("python", String.Empty),
                new TrainingPythonLauncher("py", "-3"),
                new TrainingPythonLauncher("py", String.Empty)
            }
            : new[]
            {
                new TrainingPythonLauncher("python3", String.Empty),
                new TrainingPythonLauncher("python", String.Empty)
            };

        foreach (var candidate in candidates)
        {
            if (CanImportUltralytics(candidate))
                return candidate;
        }

        throw new InvalidOperationException("未找到可用的 Python 训练环境。请先安装 Python，并在同一个解释器里执行 pip install ultralytics。当前工具已尝试 python、py -3 等常见启动方式。");
    }

    private static String BuildTrainingArguments(String argumentPrefix, String trainingScriptPath, String datasetYamlPath, String runRoot, String runName, String baseModel, Int32 imageSize, Int32 epochs, Int32 batch, String device)
    {
        var builder = new StringBuilder();
        if (!String.IsNullOrWhiteSpace(argumentPrefix))
        {
            builder.Append(argumentPrefix);
            builder.Append(' ');
        }

        builder.Append('"');
        builder.Append(trainingScriptPath);
        builder.Append('"');
        builder.Append(" --data \"");
        builder.Append(datasetYamlPath);
        builder.Append("\" --model \"");
        builder.Append(baseModel);
        builder.Append("\" --imgsz ");
        builder.Append(imageSize.ToString(CultureInfo.InvariantCulture));
        builder.Append(" --epochs ");
        builder.Append(epochs.ToString(CultureInfo.InvariantCulture));
        builder.Append(" --batch ");
        builder.Append(batch.ToString(CultureInfo.InvariantCulture));
        builder.Append(" --device ");
        builder.Append(device);
        builder.Append(" --project \"");
        builder.Append(runRoot);
        builder.Append("\" --name ");
        builder.Append(runName);
        return builder.ToString();
    }

    private static Boolean CanImportUltralytics(TrainingPythonLauncher launcher)
    {
        try
        {
            var command = !String.IsNullOrWhiteSpace(launcher.ArgumentPrefix)
                ? $"{launcher.ArgumentPrefix} -c \"import ultralytics\""
                : "-c \"import ultralytics\"";
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = launcher.FileName,
                    Arguments = command,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
                return false;

            process.WaitForExit(5000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
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
            AnnotationFile = Path.Combine("labels", Path.GetFileName(GetLabelPath(path))).Replace('\\', '/'),
            Count = _documents[path].Annotations.Count
        }).ToArray();
    }

    private void WriteAnnotations(String imagePath, AnnotationDocument document)
    {
        var labelPath = GetLabelPath(imagePath);
        Directory.CreateDirectory(Path.GetDirectoryName(labelPath)!);
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

    private static String GetLabelPath(String imagePath)
    {
        var imageFolder = Path.GetDirectoryName(imagePath) ?? AppContext.BaseDirectory;
        var labelsFolder = Path.Combine(imageFolder, "labels");
        return Path.Combine(labelsFolder, Path.ChangeExtension(Path.GetFileName(imagePath), ".txt"));
    }

    private void RefreshImageListDisplay()
    {
        var selectedIndex = _imageListBox.SelectedIndex;
        _suppressImageSelectionChanged = true;
        _imageListBox.BeginUpdate();
        _imageListBox.Items.Clear();
        foreach (var imagePath in _imageFiles)
        {
            _imageListBox.Items.Add(BuildDisplayName(imagePath));
        }
        _imageListBox.EndUpdate();

        if (selectedIndex >= 0 && selectedIndex < _imageListBox.Items.Count)
            _imageListBox.SelectedIndex = selectedIndex;

        _suppressImageSelectionChanged = false;

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
    private const Single ResizeHandleSize = 10f;

    private Bitmap? _currentBitmap;
    private List<AnnotationBox>? _annotations;
    private IReadOnlyList<String> _labels = [];
    private RectangleF _imageBounds;
    private Boolean _isDrawing;
    private Boolean _isDraggingSelection;
    private Boolean _isResizingSelection;
    private Boolean _isPanning;
    private Point _dragStart;
    private Point _dragEnd;
    private Point _lastDragPoint;
    private Point _lastPanPoint;
    private Int32 _selectedIndex = -1;
    private Single _zoom = 1f;
    private PointF _panOffset = PointF.Empty;
    private ResizeHandle _activeResizeHandle = ResizeHandle.None;

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

    public void ResetView()
    {
        _zoom = 1f;
        _panOffset = PointF.Empty;
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

        _imageBounds = CalculateImageBounds(_currentBitmap.Size, ClientRectangle, _zoom, _panOffset);
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

        if (e.Button == MouseButtons.Middle)
        {
            _isPanning = true;
            _lastPanPoint = e.Location;
            Capture = true;
            Cursor = Cursors.Hand;
            return;
        }

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
                _activeResizeHandle = HitTestResizeHandle(e.Location, _annotations[selectedIndex].NormalizedBox);
                if (_activeResizeHandle != ResizeHandle.None)
                {
                    _isResizingSelection = true;
                    Capture = true;
                    Invalidate();
                    return;
                }

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
        if (_isPanning)
        {
            _panOffset = new PointF(_panOffset.X + (e.X - _lastPanPoint.X), _panOffset.Y + (e.Y - _lastPanPoint.Y));
            _lastPanPoint = e.Location;
            Invalidate();
            return;
        }

        if (_isDraggingSelection)
        {
            DragSelectedAnnotation(e.Location);
            return;
        }

        if (_isResizingSelection)
        {
            ResizeSelectedAnnotation(e.Location);
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
        if (_isPanning)
        {
            _isPanning = false;
            Capture = false;
            Cursor = Cursors.Default;
            return;
        }

        if (_isDraggingSelection)
        {
            _isDraggingSelection = false;
            Capture = false;
            return;
        }

        if (_isResizingSelection)
        {
            _isResizingSelection = false;
            _activeResizeHandle = ResizeHandle.None;
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

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_currentBitmap == null)
            return;

        var previousZoom = _zoom;
        _zoom = e.Delta > 0 ? MathF.Min(8f, _zoom * 1.15f) : MathF.Max(0.2f, _zoom / 1.15f);
        if (Math.Abs(previousZoom - _zoom) < Single.Epsilon)
            return;

        var scaleFactor = _zoom / previousZoom;
        _panOffset = new PointF(
            (e.X - Width / 2f) - ((e.X - Width / 2f - _panOffset.X) * scaleFactor),
            (e.Y - Height / 2f) - ((e.Y - Height / 2f - _panOffset.Y) * scaleFactor));
        Invalidate();
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);
        ResetView();
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

    private void ResizeSelectedAnnotation(Point point)
    {
        if (!HasValidSelection() || _activeResizeHandle == ResizeHandle.None)
            return;

        var current = _annotations![_selectedIndex].NormalizedBox;
        var left = current.Left;
        var top = current.Top;
        var right = current.Right;
        var bottom = current.Bottom;
        var normalizedPoint = ConvertDisplayPointToNormalized(point);

        switch (_activeResizeHandle)
        {
            case ResizeHandle.TopLeft:
                left = normalizedPoint.X;
                top = normalizedPoint.Y;
                break;
            case ResizeHandle.TopRight:
                right = normalizedPoint.X;
                top = normalizedPoint.Y;
                break;
            case ResizeHandle.BottomLeft:
                left = normalizedPoint.X;
                bottom = normalizedPoint.Y;
                break;
            case ResizeHandle.BottomRight:
                right = normalizedPoint.X;
                bottom = normalizedPoint.Y;
                break;
        }

        const Single minSize = 0.005f;
        left = Math.Clamp(left, 0f, Math.Max(0f, right - minSize));
        top = Math.Clamp(top, 0f, Math.Max(0f, bottom - minSize));
        right = Math.Clamp(right, Math.Min(1f, left + minSize), 1f);
        bottom = Math.Clamp(bottom, Math.Min(1f, top + minSize), 1f);

        _annotations[_selectedIndex].NormalizedBox = RectangleF.FromLTRB(left, top, right, bottom);
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

    private ResizeHandle HitTestResizeHandle(Point point, RectangleF normalizedBox)
    {
        var rect = ConvertNormalizedToDisplay(normalizedBox);
        foreach (var handle in GetResizeHandles(rect))
        {
            if (handle.Bounds.Contains(point))
                return handle.Handle;
        }

        return ResizeHandle.None;
    }

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

        if (highlight)
        {
            using var handleBrush = new SolidBrush(Color.White);
            using var handlePen = new Pen(color, 1.5f);
            foreach (var handle in GetResizeHandles(rect))
            {
                graphics.FillRectangle(handleBrush, handle.Bounds);
                graphics.DrawRectangle(handlePen, handle.Bounds.X, handle.Bounds.Y, handle.Bounds.Width, handle.Bounds.Height);
            }
        }
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

    private PointF ConvertDisplayPointToNormalized(Point point)
    {
        var x = Math.Clamp((point.X - _imageBounds.Left) / _imageBounds.Width, 0f, 1f);
        var y = Math.Clamp((point.Y - _imageBounds.Top) / _imageBounds.Height, 0f, 1f);
        return new PointF(x, y);
    }

    private static RectangleF NormalizeDisplayRectangle(Point first, Point second)
    {
        var left = Math.Min(first.X, second.X);
        var top = Math.Min(first.Y, second.Y);
        var right = Math.Max(first.X, second.X);
        var bottom = Math.Max(first.Y, second.Y);
        return RectangleF.FromLTRB(left, top, right, bottom);
    }

    private static RectangleF CalculateImageBounds(Size imageSize, Rectangle clientRectangle, Single zoom, PointF panOffset)
    {
        const Int32 padding = 12;
        var availableWidth = Math.Max(1, clientRectangle.Width - padding * 2);
        var availableHeight = Math.Max(1, clientRectangle.Height - padding * 2);
        var scale = Math.Min(availableWidth / (Single)imageSize.Width, availableHeight / (Single)imageSize.Height);
        var width = imageSize.Width * scale * zoom;
        var height = imageSize.Height * scale * zoom;
        var x = clientRectangle.Left + (clientRectangle.Width - width) / 2f + panOffset.X;
        var y = clientRectangle.Top + (clientRectangle.Height - height) / 2f + panOffset.Y;
        return new RectangleF(x, y, width, height);
    }

    private static Color PickColor(Int32 classId)
    {
        Color[] palette = [Color.Orange, Color.LimeGreen, Color.DeepSkyBlue, Color.HotPink, Color.Gold, Color.Cyan];
        return palette[Math.Abs(classId) % palette.Length];
    }

    private static IEnumerable<ResizeHandleInfo> GetResizeHandles(RectangleF rect)
    {
        var half = ResizeHandleSize / 2f;
        yield return new ResizeHandleInfo(ResizeHandle.TopLeft, new RectangleF(rect.Left - half, rect.Top - half, ResizeHandleSize, ResizeHandleSize));
        yield return new ResizeHandleInfo(ResizeHandle.TopRight, new RectangleF(rect.Right - half, rect.Top - half, ResizeHandleSize, ResizeHandleSize));
        yield return new ResizeHandleInfo(ResizeHandle.BottomLeft, new RectangleF(rect.Left - half, rect.Bottom - half, ResizeHandleSize, ResizeHandleSize));
        yield return new ResizeHandleInfo(ResizeHandle.BottomRight, new RectangleF(rect.Right - half, rect.Bottom - half, ResizeHandleSize, ResizeHandleSize));
    }
}

internal enum ResizeHandle
{
    None,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

internal readonly record struct ResizeHandleInfo(ResizeHandle Handle, RectangleF Bounds);

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

internal sealed class StampPreAnnotationDetector
{
    public IReadOnlyList<DetectedAnnotation> Detect(String imagePath)
    {
        using var source = Cv2.ImRead(imagePath, CvImreadModes.Color);
        if (source.Empty())
            return [];

        var redDetections = DetectFromMask(source, BuildRedSealMask(source), "seal", allowPartial: true);
        var neutralDetections = DetectFromMask(source, BuildNeutralSealMask(source), "seal", allowPartial: true);

        if (redDetections.Count > 0)
            neutralDetections = FilterNeutralDetectionsNearRed(redDetections, neutralDetections);

        var allDetections = new List<DetectedAnnotation>(redDetections.Count + neutralDetections.Count);
        allDetections.AddRange(redDetections);
        allDetections.AddRange(neutralDetections);
        var filtered = ApplyNms(allDetections, 0.3f);
        return MergeNearby(filtered, source.Width, source.Height);
    }

    private static List<DetectedAnnotation> DetectFromMask(CvMat source, CvMat mask, String label, Boolean allowPartial)
    {
        using var ownedMask = mask;
        Cv2.FindContours(ownedMask, out var contours, out _, CvRetrievalModes.External, CvContourApproximationModes.ApproxSimple);
        var detections = new List<DetectedAnnotation>();
        var imageArea = source.Width * source.Height;

        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            if (area < 120 || area > imageArea * 0.35)
                continue;

            var perimeter = Cv2.ArcLength(contour, true);
            if (perimeter <= 0)
                continue;

            var circularity = 4 * Math.PI * area / (perimeter * perimeter);
            var rect = Cv2.BoundingRect(contour);
            var aspectRatio = rect.Width / (Single)Math.Max(1, rect.Height);
            if (aspectRatio is < 0.22f or > 4.5f)
                continue;

            var rectArea = rect.Width * rect.Height;
            if (rectArea <= 0)
                continue;

            var fillRatio = (Single)(area / rectArea);
            var touchesEdge = rect.Left <= 6 || rect.Top <= 6 || rect.Right >= source.Width - 6 || rect.Bottom >= source.Height - 6;
            var partialCandidate = allowPartial && (touchesEdge || circularity < 0.22 || fillRatio < 0.2f);

            if (!partialCandidate && circularity < 0.12 && (aspectRatio < 0.7f || aspectRatio > 1.4f))
                continue;

            if (partialCandidate && fillRatio < 0.06f)
                continue;

            using var roi = new CvMat(source, rect);
            var edgeDensity = ComputeEdgeDensity(roi);
            var strokeDensity = ComputeStrokeDensity(ownedMask, rect);
            if (edgeDensity < 0.015f && strokeDensity < 0.04f)
                continue;

            var score = 0.2f;
            score += MathF.Min(0.3f, MathF.Max(0f, (Single)circularity * 0.35f));
            score += MathF.Min(0.2f, fillRatio * 0.3f);
            score += MathF.Min(0.2f, edgeDensity * 1.4f);
            score += MathF.Min(0.2f, strokeDensity * 0.8f);
            if (partialCandidate)
                score = MathF.Max(score, 0.38f);

            detections.Add(new DetectedAnnotation(Normalize(rect, source.Width, source.Height), MathF.Min(0.95f, score), partialCandidate ? "partial-seal" : label));
        }

        return detections;
    }

    private static CvMat BuildRedSealMask(CvMat source)
    {
        using var hsv = new CvMat();
        using var mask1 = new CvMat();
        using var mask2 = new CvMat();
        var mask = new CvMat();
        using var morph = new CvMat();
        Cv2.CvtColor(source, hsv, CvColorConversionCodes.BGR2HSV);
        Cv2.InRange(hsv, new CvScalar(0, 80, 60), new CvScalar(15, 255, 255), mask1);
        Cv2.InRange(hsv, new CvScalar(160, 80, 60), new CvScalar(180, 255, 255), mask2);
        Cv2.BitwiseOr(mask1, mask2, mask);
        using var kernel = Cv2.GetStructuringElement(CvMorphShapes.Ellipse, new CvSize(5, 5));
        Cv2.MorphologyEx(mask, morph, CvMorphTypes.Close, kernel, iterations: 2);
        Cv2.MorphologyEx(morph, morph, CvMorphTypes.Open, kernel, iterations: 1);
        morph.CopyTo(mask);
        return mask;
    }

    private static CvMat BuildNeutralSealMask(CvMat source)
    {
        using var gray = new CvMat();
        using var blur = new CvMat();
        using var adaptive = new CvMat();
        using var edges = new CvMat();
        using var gradient = new CvMat();
        using var texture = new CvMat();
        using var merged = new CvMat();
        var mask = new CvMat();
        Cv2.CvtColor(source, gray, CvColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(gray, blur, new CvSize(5, 5), 0);
        Cv2.AdaptiveThreshold(blur, adaptive, 255, CvAdaptiveThresholdTypes.GaussianC, CvThresholdTypes.BinaryInv, 31, 8);
        Cv2.Canny(blur, edges, 60, 180);
        Cv2.Laplacian(blur, gradient, CvMatType.CV_8U, 3);
        Cv2.Threshold(gradient, texture, 24, 255, CvThresholdTypes.Binary);
        Cv2.BitwiseOr(adaptive, edges, merged);
        Cv2.BitwiseOr(merged, texture, mask);
        using var kernel = Cv2.GetStructuringElement(CvMorphShapes.Ellipse, new CvSize(5, 5));
        Cv2.MorphologyEx(mask, mask, CvMorphTypes.Close, kernel, iterations: 2);
        Cv2.MorphologyEx(mask, mask, CvMorphTypes.Open, kernel, iterations: 1);
        Cv2.Dilate(mask, mask, kernel, iterations: 1);
        return mask;
    }

    private static Single ComputeEdgeDensity(CvMat roi)
    {
        using var gray = new CvMat();
        using var edges = new CvMat();
        Cv2.CvtColor(roi, gray, CvColorConversionCodes.BGR2GRAY);
        Cv2.Canny(gray, edges, 50, 150);
        return Cv2.CountNonZero(edges) / (Single)Math.Max(1, roi.Width * roi.Height);
    }

    private static Single ComputeStrokeDensity(CvMat mask, CvRect rect)
    {
        using var roi = new CvMat(mask, rect);
        return Cv2.CountNonZero(roi) / (Single)Math.Max(1, rect.Width * rect.Height);
    }

    private static List<DetectedAnnotation> FilterNeutralDetectionsNearRed(IReadOnlyList<DetectedAnnotation> redDetections, IReadOnlyList<DetectedAnnotation> neutralDetections)
    {
        var kept = new List<DetectedAnnotation>();
        foreach (var neutral in neutralDetections)
        {
            if (redDetections.Any(red => AreNear(red.NormalizedBox, neutral.NormalizedBox)))
                kept.Add(neutral);
        }

        return kept;
    }

    private static Boolean AreNear(RectangleF first, RectangleF second)
    {
        var union = RectangleF.FromLTRB(Math.Min(first.Left, second.Left), Math.Min(first.Top, second.Top), Math.Max(first.Right, second.Right), Math.Max(first.Bottom, second.Bottom));
        var maxWidth = Math.Max(first.Width, second.Width);
        var maxHeight = Math.Max(first.Height, second.Height);
        var overlap = ComputeIou(first, second);
        if (overlap > 0)
            return true;

        var centerDeltaX = Math.Abs((first.Left + first.Right) - (second.Left + second.Right)) / 2f;
        var centerDeltaY = Math.Abs((first.Top + first.Bottom) - (second.Top + second.Bottom)) / 2f;
        return centerDeltaX <= maxWidth * 0.9f && centerDeltaY <= maxHeight * 0.9f && union.Width <= maxWidth * 1.9f && union.Height <= maxHeight * 1.9f;
    }

    private static IReadOnlyList<DetectedAnnotation> ApplyNms(IReadOnlyList<DetectedAnnotation> detections, Single iouThreshold)
    {
        if (detections.Count <= 1)
            return detections;

        var ordered = detections.OrderByDescending(item => item.Score).ToList();
        var kept = new List<DetectedAnnotation>();
        while (ordered.Count > 0)
        {
            var current = ordered[0];
            kept.Add(current);
            ordered.RemoveAt(0);
            ordered.RemoveAll(candidate => ComputeIou(current.NormalizedBox, candidate.NormalizedBox) >= iouThreshold);
        }

        return kept;
    }

    private static IReadOnlyList<DetectedAnnotation> MergeNearby(IReadOnlyList<DetectedAnnotation> detections, Int32 imageWidth, Int32 imageHeight)
    {
        if (detections.Count <= 1)
            return detections;

        var pending = detections.ToList();
        var merged = true;
        while (merged)
        {
            merged = false;
            for (var firstIndex = 0; firstIndex < pending.Count && !merged; firstIndex++)
            {
                for (var secondIndex = firstIndex + 1; secondIndex < pending.Count; secondIndex++)
                {
                    if (!ShouldMerge(pending[firstIndex].NormalizedBox, pending[secondIndex].NormalizedBox))
                        continue;

                    var union = RectangleF.FromLTRB(
                        Math.Min(pending[firstIndex].NormalizedBox.Left, pending[secondIndex].NormalizedBox.Left),
                        Math.Min(pending[firstIndex].NormalizedBox.Top, pending[secondIndex].NormalizedBox.Top),
                        Math.Max(pending[firstIndex].NormalizedBox.Right, pending[secondIndex].NormalizedBox.Right),
                        Math.Max(pending[firstIndex].NormalizedBox.Bottom, pending[secondIndex].NormalizedBox.Bottom));
                    var label = pending[firstIndex].Label == "partial-seal" || pending[secondIndex].Label == "partial-seal" ? "partial-seal" : "seal";
                    pending[firstIndex] = new DetectedAnnotation(union, MathF.Max(pending[firstIndex].Score, pending[secondIndex].Score), label);
                    pending.RemoveAt(secondIndex);
                    merged = true;
                    break;
                }
            }
        }

        return pending;
    }

    private static Boolean ShouldMerge(RectangleF first, RectangleF second)
    {
        var union = RectangleF.FromLTRB(Math.Min(first.Left, second.Left), Math.Min(first.Top, second.Top), Math.Max(first.Right, second.Right), Math.Max(first.Bottom, second.Bottom));
        var maxWidth = Math.Max(first.Width, second.Width);
        var maxHeight = Math.Max(first.Height, second.Height);
        var gapX = Math.Max(0, Math.Max(first.Left, second.Left) - Math.Min(first.Right, second.Right));
        var gapY = Math.Max(0, Math.Max(first.Top, second.Top) - Math.Min(first.Bottom, second.Bottom));
        var centerDeltaX = Math.Abs((first.Left + first.Right) - (second.Left + second.Right)) / 2f;
        var centerDeltaY = Math.Abs((first.Top + first.Bottom) - (second.Top + second.Bottom)) / 2f;
        var closeEnough = gapX <= maxWidth * 0.6f && gapY <= maxHeight * 0.6f;
        var sameNeighborhood = centerDeltaX <= union.Width * 0.75f && centerDeltaY <= union.Height * 0.75f;
        var reasonableUnion = union.Width <= maxWidth * 1.8f && union.Height <= maxHeight * 1.8f;
        return closeEnough && sameNeighborhood && reasonableUnion;
    }

    private static Single ComputeIou(RectangleF first, RectangleF second)
    {
        var x1 = Math.Max(first.Left, second.Left);
        var y1 = Math.Max(first.Top, second.Top);
        var x2 = Math.Min(first.Right, second.Right);
        var y2 = Math.Min(first.Bottom, second.Bottom);
        var intersectionWidth = Math.Max(0, x2 - x1);
        var intersectionHeight = Math.Max(0, y2 - y1);
        var intersection = intersectionWidth * intersectionHeight;
        if (intersection <= 0)
            return 0;

        var union = first.Width * first.Height + second.Width * second.Height - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    private static RectangleF Normalize(CvRect rect, Int32 width, Int32 height)
    {
        return new RectangleF(
            rect.X / (Single)Math.Max(1, width),
            rect.Y / (Single)Math.Max(1, height),
            rect.Width / (Single)Math.Max(1, width),
            rect.Height / (Single)Math.Max(1, height));
    }
}

internal sealed record DetectedAnnotation(RectangleF NormalizedBox, Single Score, String Label);

internal sealed class OnnxPreAnnotationDetector : IDisposable
{
    private readonly InferenceSession _session;
    private readonly IReadOnlyList<String> _labels;
    private readonly String _inputName;
    private readonly Int32 _inputSize;
    private readonly Single _confidenceThreshold;
    private readonly Single _iouThreshold;

    public OnnxPreAnnotationDetector(String modelPath, IReadOnlyList<String> labels, Int32 inputSize = 640, Single confidenceThreshold = 0.25f, Single iouThreshold = 0.45f)
    {
        _session = new InferenceSession(modelPath);
        _labels = labels;
        _inputName = _session.InputMetadata.Keys.First();
        _inputSize = inputSize;
        _confidenceThreshold = confidenceThreshold;
        _iouThreshold = iouThreshold;
    }

    public IReadOnlyList<DetectedAnnotation> Detect(String imagePath)
    {
        using var source = Cv2.ImRead(imagePath, CvImreadModes.Color);
        if (source.Empty())
            return [];

        var (tensor, ratio, padX, padY) = Letterbox(source, _inputSize);
        var input = NamedOnnxValue.CreateFromTensor(_inputName, tensor);
        using var results = _session.Run([input]);
        var output = results.First().AsTensor<Single>();
        var detections = Decode(output, ratio, padX, padY, source.Width, source.Height, _labels, _confidenceThreshold);
        return ApplyNms(detections, _iouThreshold);
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    private static (DenseTensor<Single> Tensor, Single Ratio, Int32 PadX, Int32 PadY) Letterbox(CvMat source, Int32 inputSize)
    {
        var ratio = Math.Min(inputSize / (Single)source.Width, inputSize / (Single)source.Height);
        var resizedWidth = Math.Max(1, (Int32)Math.Round(source.Width * ratio));
        var resizedHeight = Math.Max(1, (Int32)Math.Round(source.Height * ratio));
        var padX = (inputSize - resizedWidth) / 2;
        var padY = (inputSize - resizedHeight) / 2;

        using var resized = new CvMat();
        using var letterbox = new CvMat(new CvSize(inputSize, inputSize), CvMatType.CV_8UC3, new CvScalar(114, 114, 114));
        Cv2.Resize(source, resized, new CvSize(resizedWidth, resizedHeight));
        resized.CopyTo(new CvMat(letterbox, new CvRect(padX, padY, resizedWidth, resizedHeight)));

        var tensor = new DenseTensor<Single>([1, 3, inputSize, inputSize]);
        for (var y = 0; y < inputSize; y++)
        {
            for (var x = 0; x < inputSize; x++)
            {
                var pixel = letterbox.At<OpenCvSharp.Vec3b>(y, x);
                tensor[0, 0, y, x] = pixel.Item2 / 255f;
                tensor[0, 1, y, x] = pixel.Item1 / 255f;
                tensor[0, 2, y, x] = pixel.Item0 / 255f;
            }
        }

        return (tensor, ratio, padX, padY);
    }

    private static List<DetectedAnnotation> Decode(Tensor<Single> output, Single ratio, Int32 padX, Int32 padY, Int32 originalWidth, Int32 originalHeight, IReadOnlyList<String> labels, Single confidenceThreshold)
    {
        var dimensions = output.Dimensions.ToArray();
        if (dimensions.Length != 3)
            throw new NotSupportedException($"当前预标注仅支持三维 YOLO 输出，实际输出维度为 {dimensions.Length}");

        var detections = new List<DetectedAnnotation>();
        if (dimensions[1] >= 6 && dimensions[2] > dimensions[1])
        {
            var featureCount = dimensions[1];
            var boxCount = dimensions[2];
            for (var index = 0; index < boxCount; index++)
            {
                AppendDetection(output, labels, confidenceThreshold, detections, output[0, 0, index], output[0, 1, index], output[0, 2, index], output[0, 3, index], featureCount, index, ratio, padX, padY, originalWidth, originalHeight);
            }
        }
        else
        {
            var boxCount = dimensions[1];
            var featureCount = dimensions[2];
            for (var index = 0; index < boxCount; index++)
            {
                AppendDetection(output, labels, confidenceThreshold, detections, output[0, index, 0], output[0, index, 1], output[0, index, 2], output[0, index, 3], featureCount, index, ratio, padX, padY, originalWidth, originalHeight, channelLast: true);
            }
        }

        return detections;
    }

    private static void AppendDetection(Tensor<Single> output, IReadOnlyList<String> labels, Single confidenceThreshold, List<DetectedAnnotation> detections, Single centerX, Single centerY, Single width, Single height, Int32 featureCount, Int32 index, Single ratio, Int32 padX, Int32 padY, Int32 originalWidth, Int32 originalHeight, Boolean channelLast = false)
    {
        var bestClass = 0;
        var bestScore = 0f;
        for (var classIndex = 4; classIndex < featureCount; classIndex++)
        {
            var score = channelLast ? output[0, index, classIndex] : output[0, classIndex, index];
            if (score > bestScore)
            {
                bestScore = score;
                bestClass = classIndex - 4;
            }
        }

        if (bestScore < confidenceThreshold)
            return;

        var x1 = (centerX - width / 2 - padX) / ratio;
        var y1 = (centerY - height / 2 - padY) / ratio;
        var x2 = (centerX + width / 2 - padX) / ratio;
        var y2 = (centerY + height / 2 - padY) / ratio;
        var left = Math.Clamp((Single)(x1 / Math.Max(1, originalWidth)), 0f, 1f);
        var top = Math.Clamp((Single)(y1 / Math.Max(1, originalHeight)), 0f, 1f);
        var right = Math.Clamp((Single)(x2 / Math.Max(1, originalWidth)), left + 0.001f, 1f);
        var bottom = Math.Clamp((Single)(y2 / Math.Max(1, originalHeight)), top + 0.001f, 1f);
        var label = bestClass < labels.Count ? labels[bestClass] : $"class_{bestClass}";
        detections.Add(new DetectedAnnotation(RectangleF.FromLTRB(left, top, right, bottom), bestScore, label));
    }

    private static IReadOnlyList<DetectedAnnotation> ApplyNms(IReadOnlyList<DetectedAnnotation> detections, Single iouThreshold)
    {
        if (detections.Count <= 1)
            return detections;

        var ordered = detections.OrderByDescending(item => item.Score).ToList();
        var kept = new List<DetectedAnnotation>();
        while (ordered.Count > 0)
        {
            var current = ordered[0];
            kept.Add(current);
            ordered.RemoveAt(0);
            ordered.RemoveAll(candidate => ComputeIou(current.NormalizedBox, candidate.NormalizedBox) >= iouThreshold);
        }

        return kept;
    }

    private static Single ComputeIou(RectangleF first, RectangleF second)
    {
        var x1 = Math.Max(first.Left, second.Left);
        var y1 = Math.Max(first.Top, second.Top);
        var x2 = Math.Min(first.Right, second.Right);
        var y2 = Math.Min(first.Bottom, second.Bottom);
        var intersectionWidth = Math.Max(0, x2 - x1);
        var intersectionHeight = Math.Max(0, y2 - y1);
        var intersection = intersectionWidth * intersectionHeight;
        if (intersection <= 0)
            return 0;

        var union = first.Width * first.Height + second.Width * second.Height - intersection;
        return union <= 0 ? 0 : intersection / union;
    }
}

internal sealed record TrainingProcessResult(Int32 ExitCode, String Output);

internal sealed record TrainingPythonLauncher(String FileName, String ArgumentPrefix)
{
    public String DisplayName => String.IsNullOrWhiteSpace(ArgumentPrefix) ? FileName : $"{FileName} {ArgumentPrefix}";
}