using System.Globalization;
using OpenCvSharp;

namespace Pek.AI.StampDetectionDemo;

internal static class Program
{
    private static Int32 Main(String[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            var options = StampDetectionOptions.Parse(args);
            var detector = new StampDetectionPipeline(options);
            var result = detector.Run();

            Console.WriteLine($"输入文件: {options.InputPath}");
            Console.WriteLine($"输出目录: {result.OutputDirectory}");
            Console.WriteLine($"总页数: {result.Pages.Count}");
            Console.WriteLine($"总印章数: {result.TotalCount}");
            Console.WriteLine();

            foreach (var page in result.Pages)
            {
                Console.WriteLine($"第 {page.PageNumber} 页: OpenCV 候选 {page.OpenCvCount} 个, ONNX 检测 {page.OnnxCount} 个, 最终计数 {page.FinalCount} 个");
                Console.WriteLine($"标注图: {page.AnnotatedImagePath}");
            }

            if (!String.IsNullOrWhiteSpace(result.Warning))
                Console.WriteLine($"提示: {result.Warning}");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("执行失败:");
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("印章计数 Demo");
        Console.WriteLine("示例:");
        Console.WriteLine("  dotnet run --project Pek.AI.StampDetectionDemo -- --input sample.jpg");
        Console.WriteLine("  dotnet run --project Pek.AI.StampDetectionDemo -- --input sample.pdf --model seal.onnx --labels seal");
        Console.WriteLine("  dotnet run --project Pek.AI.StampDetectionDemo -- --input sample.pdf --model seal.onnx --labels-file labels.txt");
        Console.WriteLine();
        Console.WriteLine("参数:");
        Console.WriteLine("  --input   必填，图片或 PDF 路径");
        Console.WriteLine("  --model   可选，YOLO ONNX 模型路径");
        Console.WriteLine("  --output  可选，输出目录，默认在输入文件旁边生成 output 时间戳目录");
        Console.WriteLine("  --conf    可选，置信度阈值，默认 0.25");
        Console.WriteLine("  --iou     可选，NMS 阈值，默认 0.45");
        Console.WriteLine("  --size    可选，输入尺寸，默认 640");
        Console.WriteLine("  --labels  可选，类别名，逗号分隔，默认 seal");
        Console.WriteLine("  --labels-file 可选，标签文件路径，每行一个类别名");
    }
}

internal sealed class StampDetectionOptions
{
    public required String InputPath { get; init; }

    public String? ModelPath { get; init; }

    public required String OutputDirectory { get; init; }

    public Single ConfidenceThreshold { get; init; } = 0.25f;

    public Single IouThreshold { get; init; } = 0.45f;

    public Int32 InputSize { get; init; } = 640;

    public IReadOnlyList<String> Labels { get; init; } = ["seal"];

    public String? LabelsFilePath { get; init; }

    public static StampDetectionOptions Parse(String[] args)
    {
        String? inputPath = null;
        String? modelPath = null;
        String? outputDirectory = null;
        String? labelsFilePath = null;
        var confidenceThreshold = 0.25f;
        var iouThreshold = 0.45f;
        var inputSize = 640;
        IReadOnlyList<String> labels = ["seal"];

        for (var index = 0; index < args.Length; index++)
        {
            var key = args[index];
            if (!key.StartsWith("--", StringComparison.Ordinal))
                continue;

            if (index + 1 >= args.Length)
                throw new ArgumentException($"参数 {key} 缺少值");

            var value = args[++index];
            switch (key)
            {
                case "--input":
                    inputPath = Path.GetFullPath(value);
                    break;
                case "--model":
                    modelPath = Path.GetFullPath(value);
                    break;
                case "--output":
                    outputDirectory = Path.GetFullPath(value);
                    break;
                case "--conf":
                    confidenceThreshold = Single.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--iou":
                    iouThreshold = Single.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--size":
                    inputSize = Int32.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--labels":
                    labels = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;
                case "--labels-file":
                    labelsFilePath = Path.GetFullPath(value);
                    break;
                default:
                    throw new ArgumentException($"未知参数: {key}");
            }
        }

        if (String.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("必须提供 --input 参数");

        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"输入文件不存在: {inputPath}");

        var inputDirectory = Path.GetDirectoryName(inputPath) ?? AppContext.BaseDirectory;
        if (String.IsNullOrWhiteSpace(modelPath))
        {
            var autoModelPath = Path.Combine(inputDirectory, "model", "stamp-detector.onnx");
            if (File.Exists(autoModelPath))
                modelPath = autoModelPath;
        }

        if (String.IsNullOrWhiteSpace(labelsFilePath))
        {
            var autoLabelsPath = Path.Combine(inputDirectory, "model", "labels.txt");
            if (File.Exists(autoLabelsPath))
                labelsFilePath = autoLabelsPath;
        }

        if (!String.IsNullOrWhiteSpace(modelPath) && !File.Exists(modelPath))
            throw new FileNotFoundException($"模型文件不存在: {modelPath}");

        if (!String.IsNullOrWhiteSpace(labelsFilePath))
        {
            if (!File.Exists(labelsFilePath))
                throw new FileNotFoundException($"标签文件不存在: {labelsFilePath}");

            labels = File.ReadAllLines(labelsFilePath)
                .Select(item => item.Trim())
                .Where(item => !String.IsNullOrWhiteSpace(item))
                .ToArray();

            if (labels.Count == 0)
                throw new ArgumentException($"标签文件为空: {labelsFilePath}");
        }

        outputDirectory ??= Path.Combine(
            Path.GetDirectoryName(inputPath) ?? AppContext.BaseDirectory,
            $"output_{DateTime.Now:yyyyMMdd_HHmmss}");

        return new StampDetectionOptions
        {
            InputPath = inputPath,
            ModelPath = modelPath,
            OutputDirectory = outputDirectory,
            ConfidenceThreshold = confidenceThreshold,
            IouThreshold = iouThreshold,
            InputSize = inputSize,
            Labels = labels,
            LabelsFilePath = labelsFilePath
        };
    }
}

internal sealed class StampDetectionPipeline
{
    private readonly StampDetectionOptions _options;
    private readonly OpenCvSealCandidateDetector _openCvDetector;
    private readonly YoloOnnxSealDetector? _onnxDetector;

    public StampDetectionPipeline(StampDetectionOptions options)
    {
        _options = options;
        _openCvDetector = new OpenCvSealCandidateDetector();

        if (!String.IsNullOrWhiteSpace(options.ModelPath))
            _onnxDetector = new YoloOnnxSealDetector(options);
    }

    public StampDetectionRunResult Run()
    {
        Directory.CreateDirectory(_options.OutputDirectory);
        var pages = LoadPages(_options.InputPath);
        var results = new List<StampPageResult>(pages.Count);
        String? warning = null;

        foreach (var page in pages)
        {
            using var source = page.Image;
            var openCvDetections = _openCvDetector.Detect(source);
            IReadOnlyList<SealDetection> onnxDetections = [];

            if (_onnxDetector != null)
            {
                onnxDetections = _onnxDetector.Detect(source);
            }
            else
            {
                warning ??= "未提供 ONNX 模型，当前最终计数采用 OpenCV 候选结果。";
            }

            var finalDetections = onnxDetections.Count > 0 ? onnxDetections : openCvDetections;
            var annotatedPath = Path.Combine(_options.OutputDirectory, $"page_{page.PageNumber:000}.png");
            using var annotated = source.Clone();
            if (onnxDetections.Count > 0)
            {
                DrawDetections(annotated, finalDetections, Scalar.LimeGreen, "ONNX");
            }
            else
            {
                DrawDetections(annotated, openCvDetections, Scalar.Orange, "CV");
            }
            Cv2.ImWrite(annotatedPath, annotated);

            results.Add(new StampPageResult(
                page.PageNumber,
                openCvDetections.Count,
                onnxDetections.Count,
                finalDetections.Count,
                annotatedPath));
        }

        return new StampDetectionRunResult(results, _options.OutputDirectory, warning);
    }

    private static List<PageImage> LoadPages(String inputPath)
    {
        var extension = Path.GetExtension(inputPath).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => PdfPageRasterizer.Rasterize(inputPath),
            ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tif" or ".tiff" or ".webp" =>
                [new PageImage(1, Cv2.ImRead(inputPath, ImreadModes.Color))],
            _ => throw new NotSupportedException($"不支持的输入格式: {extension}")
        };
    }

    private static void DrawDetections(Mat image, IReadOnlyList<SealDetection> detections, Scalar color, String prefix)
    {
        foreach (var detection in detections)
        {
            var displayRect = GetDisplayRectForDrawing(image, detection);
            Cv2.Rectangle(image, displayRect, color, 2);
            var caption = $"{prefix}:{detection.Score:0.00}";
            var textPoint = new Point(displayRect.X, Math.Max(18, displayRect.Y - 6));
            Cv2.PutText(image, caption, textPoint, HersheyFonts.HersheySimplex, 0.6, color, 2);
        }
    }

    private static Rect GetDisplayRectForDrawing(Mat image, SealDetection detection)
    {
        if (!detection.Source.Contains("opencv-red", StringComparison.OrdinalIgnoreCase))
            return detection.Box;

        var isPartial = detection.Label == "partial-seal";
        return TryFitRedSealDisplayRect(image, detection.Box, isPartial, out var fittedRect) ? fittedRect : detection.Box;
    }

    private static Boolean TryFitRedSealDisplayRect(Mat image, Rect detectionBox, Boolean isPartial, out Rect fittedRect)
    {
        var padded = isPartial
            ? new Rect(0, 0, image.Width, image.Height)
            : InflateRect(detectionBox, image.Width, image.Height, 18);
        using var roi = new Mat(image, padded);
        using var redMask = BuildRedDisplayMask(roi);

        Cv2.FindContours(redMask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0)
        {
            fittedRect = detectionBox;
            return false;
        }

        var focusWindow = InflateRect(
            new Rect(detectionBox.X - padded.X, detectionBox.Y - padded.Y, detectionBox.Width, detectionBox.Height),
            padded.Width,
            padded.Height,
            isPartial ? 0 : 12);
        var focusCenter = new Point2f(focusWindow.X + focusWindow.Width / 2f, focusWindow.Y + focusWindow.Height / 2f);

        var selectedContours = contours
            .Select(contour => new
            {
                Contour = contour,
                Rect = Cv2.BoundingRect(contour),
                Area = Cv2.ContourArea(contour),
                Perimeter = Cv2.ArcLength(contour, true)
            })
            .Where(item => item.Area >= 50)
            .Select(item => new RedDisplayContourCandidate(
                item.Contour,
                item.Rect,
                item.Area,
                item.Perimeter <= 0 ? 0f : (Single)(4 * Math.PI * item.Area / (item.Perimeter * item.Perimeter))))
            .Where(item => MatchesRedDisplayContour(item, focusCenter, focusWindow, detectionBox, padded, image.Width, image.Height, isPartial))
            .OrderByDescending(item => ScoreRedDisplayContour(item, focusCenter, focusWindow, detectionBox, image.Width, image.Height, isPartial))
            .Take(isPartial ? 2 : 3)
            .ToArray();

        if (selectedContours.Length == 0)
        {
            fittedRect = detectionBox;
            return false;
        }

        if (!isPartial)
        {
            var contourUnion = selectedContours
                .Select(item => item.Rect)
                .Aggregate((current, next) => UnionRects(current, next));
            var tightRect = InflateRect(
                new Rect(contourUnion.X + padded.X, contourUnion.Y + padded.Y, contourUnion.Width, contourUnion.Height),
                image.Width,
                image.Height,
                4);

            var tightAspectRatio = tightRect.Width / (Single)Math.Max(1, tightRect.Height);
            if (tightAspectRatio is > 0.75f and < 1.25f)
            {
                fittedRect = tightRect;
                return true;
            }
        }

        var allPoints = selectedContours.SelectMany(item => item.Contour).ToArray();
        if (allPoints.Length < 5)
        {
            fittedRect = detectionBox;
            return false;
        }

        Cv2.MinEnclosingCircle(allPoints, out var center, out var radius);
        if (radius <= 1f)
        {
            fittedRect = detectionBox;
            return false;
        }

        var left = Math.Clamp((Int32)MathF.Floor(center.X - radius) + padded.X, 0, Math.Max(0, image.Width - 1));
        var top = Math.Clamp((Int32)MathF.Floor(center.Y - radius) + padded.Y, 0, Math.Max(0, image.Height - 1));
        var right = Math.Clamp((Int32)MathF.Ceiling(center.X + radius) + padded.X, left + 1, image.Width);
        var bottom = Math.Clamp((Int32)MathF.Ceiling(center.Y + radius) + padded.Y, top + 1, image.Height);
        fittedRect = new Rect(left, top, right - left, bottom - top);
        return fittedRect.Width > 0 && fittedRect.Height > 0;
    }

    private static Mat BuildRedDisplayMask(Mat source)
    {
        using var hsv = new Mat();
        using var mask1 = new Mat();
        using var mask2 = new Mat();
        var redMask = new Mat();

        Cv2.CvtColor(source, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.InRange(hsv, new Scalar(0, 80, 60), new Scalar(15, 255, 255), mask1);
        Cv2.InRange(hsv, new Scalar(160, 80, 60), new Scalar(180, 255, 255), mask2);
        Cv2.BitwiseOr(mask1, mask2, redMask);
        return redMask;
    }

    private static Boolean MatchesRedDisplayContour(RedDisplayContourCandidate candidate, Point2f focusCenter, Rect focusWindow, Rect detectionBox, Rect padded, Int32 imageWidth, Int32 imageHeight, Boolean isPartial)
    {
        return isPartial
            ? MatchesPartialDisplayContour(candidate.Rect, detectionBox, imageWidth, imageHeight)
            : MatchesFullRedDisplayContour(candidate.Rect, focusCenter, focusWindow, detectionBox);
    }

    private static Boolean MatchesPartialDisplayContour(Rect contourRect, Rect detectionBox, Int32 imageWidth, Int32 imageHeight)
    {
        var touchesLeft = detectionBox.Left <= 6;
        var touchesTop = detectionBox.Top <= 6;
        var touchesRight = detectionBox.Right >= imageWidth - 6;
        var touchesBottom = detectionBox.Bottom >= imageHeight - 6;
        var contourCenterX = contourRect.X + contourRect.Width / 2f;
        var contourCenterY = contourRect.Y + contourRect.Height / 2f;
        var detectionCenterX = detectionBox.X + detectionBox.Width / 2f;
        var detectionCenterY = detectionBox.Y + detectionBox.Height / 2f;

        if (touchesLeft && contourRect.Left > 12)
            return false;

        if (touchesTop && contourRect.Top > 12)
            return false;

        if (touchesRight && contourRect.Right < imageWidth - 12)
            return false;

        if (touchesBottom && contourRect.Bottom < imageHeight - 12)
            return false;

        if ((touchesTop || touchesBottom) && !touchesLeft && !touchesRight)
        {
            var allowedCenterDeltaX = Math.Max(80f, detectionBox.Width * 0.95f);
            if (Math.Abs(contourCenterX - detectionCenterX) > allowedCenterDeltaX)
                return false;
        }

        if ((touchesLeft || touchesRight) && !touchesTop && !touchesBottom)
        {
            var allowedCenterDeltaY = Math.Max(80f, detectionBox.Height * 0.95f);
            if (Math.Abs(contourCenterY - detectionCenterY) > allowedCenterDeltaY)
                return false;
        }

        if ((touchesTop || touchesBottom) && (touchesLeft || touchesRight))
        {
            var allowedCenterDeltaX = Math.Max(96f, detectionBox.Width * 1.1f);
            var allowedCenterDeltaY = Math.Max(96f, detectionBox.Height * 1.1f);
            if (Math.Abs(contourCenterX - detectionCenterX) > allowedCenterDeltaX || Math.Abs(contourCenterY - detectionCenterY) > allowedCenterDeltaY)
                return false;
        }

        var union = UnionRects(contourRect, detectionBox);
        var maxWidth = Math.Max(contourRect.Width, detectionBox.Width);
        var maxHeight = Math.Max(contourRect.Height, detectionBox.Height);
        return union.Width <= maxWidth * 3.2f && union.Height <= maxHeight * 3.2f;
    }

    private static Single ScoreRedDisplayContour(RedDisplayContourCandidate candidate, Point2f focusCenter, Rect focusWindow, Rect detectionBox, Int32 imageWidth, Int32 imageHeight, Boolean isPartial)
    {
        return isPartial
            ? ScorePartialDisplayContour(candidate.Rect, candidate.Area, detectionBox, focusCenter)
            : ScoreFullRedDisplayContour(candidate.Rect, candidate.Area, candidate.Circularity, focusCenter, focusWindow);
    }

    private static Single ScorePartialDisplayContour(Rect contourRect, Double area, Rect detectionBox, Point2f anchorCenter)
    {
        var edgeBonus = 0f;
        if (detectionBox.Left <= 6 && contourRect.Left <= 12)
            edgeBonus += 2.5f;

        if (detectionBox.Top <= 6 && contourRect.Top <= 12)
            edgeBonus += 2.5f;

        if (detectionBox.Right >= contourRect.Right - 6 || contourRect.Right >= detectionBox.Right)
            edgeBonus += 1f;

        if (detectionBox.Bottom >= contourRect.Bottom - 6 || contourRect.Bottom >= detectionBox.Bottom)
            edgeBonus += 1f;

        if (detectionBox.Right >= contourRect.Right - 6 && contourRect.Right >= detectionBox.Right)
            edgeBonus += 1.5f;

        if (detectionBox.Bottom >= contourRect.Bottom - 6 && contourRect.Bottom >= detectionBox.Bottom)
            edgeBonus += 1.5f;

        var sizeBonus = MathF.Min(6f, MathF.Max(contourRect.Width, contourRect.Height) / Math.Max(1f, MathF.Max(detectionBox.Width, detectionBox.Height)));
        var areaBonus = MathF.Min(4f, (Single)Math.Sqrt(Math.Max(1d, area)) / 20f);
        var distancePenalty = DistanceToRectCenter(contourRect, anchorCenter) / Math.Max(20f, MathF.Max(detectionBox.Width, detectionBox.Height));
        return edgeBonus + sizeBonus + areaBonus - distancePenalty;
    }

    private static Single ScoreFullRedDisplayContour(Rect contourRect, Double area, Single circularity, Point2f focusCenter, Rect focusWindow)
    {
        var areaBonus = MathF.Min(4f, (Single)Math.Sqrt(Math.Max(1d, area)) / 18f);
        var circularityBonus = MathF.Max(0f, circularity) * 5f;
        var overlapBonus = ComputeRectIouLocal(contourRect, focusWindow) * 4f;
        var distancePenalty = DistanceToRectCenter(contourRect, focusCenter) / Math.Max(24f, MathF.Max(focusWindow.Width, focusWindow.Height) * 0.4f);
        return areaBonus + circularityBonus + overlapBonus - distancePenalty;
    }

    private static Boolean MatchesFullRedDisplayContour(Rect contourRect, Point2f focusCenter, Rect focusWindow, Rect detectionBox)
    {
        if (!contourRect.IntersectsWith(focusWindow) && !ContainsPoint(contourRect, focusCenter))
            return false;

        var contourCenterX = contourRect.X + contourRect.Width / 2f;
        var contourCenterY = contourRect.Y + contourRect.Height / 2f;
        var allowedCenterDeltaX = Math.Max(28f, detectionBox.Width * 0.42f);
        var allowedCenterDeltaY = Math.Max(28f, detectionBox.Height * 0.42f);
        if (Math.Abs(contourCenterX - focusCenter.X) > allowedCenterDeltaX || Math.Abs(contourCenterY - focusCenter.Y) > allowedCenterDeltaY)
            return false;

        var maxAllowedWidth = Math.Max(detectionBox.Width * 1.2f, detectionBox.Width + 20f);
        var maxAllowedHeight = Math.Max(detectionBox.Height * 1.2f, detectionBox.Height + 20f);
        if (contourRect.Width > maxAllowedWidth || contourRect.Height > maxAllowedHeight)
            return false;

        return true;
    }

    private sealed record RedDisplayContourCandidate(Point[] Contour, Rect Rect, Double Area, Single Circularity);

    private static Boolean ContainsPoint(Rect rect, Point2f point)
    {
        return point.X >= rect.Left && point.X <= rect.Right && point.Y >= rect.Top && point.Y <= rect.Bottom;
    }

    private static Single ComputeRectIouLocal(Rect first, Rect second)
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

        var firstArea = first.Width * first.Height;
        var secondArea = second.Width * second.Height;
        var union = firstArea + secondArea - intersection;
        return union <= 0 ? 0 : intersection / (Single)union;
    }

    private static Rect UnionRects(Rect first, Rect second)
    {
        var left = Math.Min(first.Left, second.Left);
        var top = Math.Min(first.Top, second.Top);
        var right = Math.Max(first.Right, second.Right);
        var bottom = Math.Max(first.Bottom, second.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static Rect InflateRect(Rect rect, Int32 imageWidth, Int32 imageHeight, Int32 padding)
    {
        var left = Math.Max(0, rect.Left - padding);
        var top = Math.Max(0, rect.Top - padding);
        var right = Math.Min(imageWidth, rect.Right + padding);
        var bottom = Math.Min(imageHeight, rect.Bottom + padding);
        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static Single DistanceToRectCenter(Rect rect, Point2f point)
    {
        var centerX = rect.X + rect.Width / 2f;
        var centerY = rect.Y + rect.Height / 2f;
        var deltaX = centerX - point.X;
        var deltaY = centerY - point.Y;
        return MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }
}

internal static class PdfPageRasterizer
{
    public static List<PageImage> Rasterize(String pdfPath, Int32 dpi = 200)
    {
        var pages = new List<PageImage>();
        using var docReader = Docnet.Core.DocLib.Instance.GetDocReader(pdfPath, new Docnet.Core.Models.PageDimensions(dpi, dpi));
        var pageCount = docReader.GetPageCount();

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            using var pageReader = docReader.GetPageReader(pageIndex);
            var rawBytes = pageReader.GetImage();
            var width = pageReader.GetPageWidth();
            var height = pageReader.GetPageHeight();
            using var rgba = Mat.FromPixelData(height, width, MatType.CV_8UC4, rawBytes);
            var bgr = new Mat();
            Cv2.CvtColor(rgba, bgr, ColorConversionCodes.RGBA2BGR);
            pages.Add(new PageImage(pageIndex + 1, bgr));
        }

        return pages;
    }
}

internal sealed class OpenCvSealCandidateDetector
{
    private static readonly Boolean DebugCandidateLoggingEnabled = String.Equals(Environment.GetEnvironmentVariable("PEK_STAMP_DEBUG"), "1", StringComparison.Ordinal);

    public IReadOnlyList<SealDetection> Detect(Mat source)
    {
        using var redMask = BuildRedSealMask(source);
        var redDetections = DetectFromMask(source, redMask, channelName: "opencv-red", allowPartial: true).ToList();

        using var neutralMask = BuildNeutralSealMask(source);
        var neutralDetections = DetectFromMask(source, neutralMask, channelName: "opencv-neutral", allowPartial: true).ToList();

        var promotedRedDetections = neutralDetections
            .Where(item => item.Source.Contains("opencv-red-support", StringComparison.Ordinal))
            .ToList();
        if (promotedRedDetections.Count > 0)
        {
            redDetections.AddRange(promotedRedDetections);
            neutralDetections = neutralDetections
                .Where(item => !item.Source.Contains("opencv-red-support", StringComparison.Ordinal))
                .ToList();
        }

        if (DebugCandidateLoggingEnabled)
        {
            WriteDebugDetections("red/raw", redDetections);
            WriteDebugDetections("neutral/raw", neutralDetections);
        }

        var reliableRedDetections = redDetections.Where(IsReliableRedAnchor).ToList();
        if (reliableRedDetections.Count > 0)
            neutralDetections = FilterNeutralDetectionsNearRed(reliableRedDetections, neutralDetections);

        if (DebugCandidateLoggingEnabled)
            WriteDebugDetections("neutral/filtered", neutralDetections);

        var detections = new List<SealDetection>(redDetections.Count + neutralDetections.Count);
        detections.AddRange(redDetections);
        detections.AddRange(neutralDetections);

        var filtered = NonMaxSuppression.Apply(detections, 0.3f);
        var merged = MergeNearbyDetections(filtered, source.Width, source.Height);

        if (DebugCandidateLoggingEnabled)
            WriteDebugDetections("merged", merged);

        return merged;
    }

    private static void WriteDebugDetections(String stage, IReadOnlyList<SealDetection> detections)
    {
        Console.WriteLine($"[debug] {stage} count={detections.Count}");

        foreach (var detection in detections)
        {
            Console.WriteLine($"[debug] {stage} source={detection.Source} label={detection.Label} rect=({detection.Box.X},{detection.Box.Y},{detection.Box.Width},{detection.Box.Height}) score={detection.Score:0.000}");
        }
    }

    private static Boolean IsReliableRedAnchor(SealDetection detection)
    {
        if (!detection.Source.Contains("opencv-red", StringComparison.Ordinal))
            return false;

        var area = detection.Box.Width * detection.Box.Height;
        var minimumSide = Math.Min(detection.Box.Width, detection.Box.Height);
        var maximumSide = Math.Max(detection.Box.Width, detection.Box.Height);
        return area >= 1200 || (minimumSide >= 24 && maximumSide >= 48);
    }

    private static Mat BuildRedSealMask(Mat source)
    {
        using var hsv = new Mat();
        using var strictMask1 = new Mat();
        using var strictMask2 = new Mat();
        using var relaxedMask1 = new Mat();
        using var relaxedMask2 = new Mat();
        using var relaxedHueMask = new Mat();
        using var redDominantMask = BuildRedDominantMask(source);
        var mask = new Mat();
        using var morph = new Mat();

        Cv2.CvtColor(source, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.InRange(hsv, new Scalar(0, 80, 60), new Scalar(15, 255, 255), strictMask1);
        Cv2.InRange(hsv, new Scalar(160, 80, 60), new Scalar(180, 255, 255), strictMask2);
        Cv2.BitwiseOr(strictMask1, strictMask2, mask);

        // 扫描件里的浅红章经常饱和度偏低，单靠严格 HSV 会漏掉，需要附加一层红通道占优约束。
        Cv2.InRange(hsv, new Scalar(0, 20, 95), new Scalar(18, 255, 255), relaxedMask1);
        Cv2.InRange(hsv, new Scalar(150, 20, 95), new Scalar(180, 255, 255), relaxedMask2);
        Cv2.BitwiseOr(relaxedMask1, relaxedMask2, relaxedHueMask);
        Cv2.BitwiseAnd(relaxedHueMask, redDominantMask, relaxedHueMask);
        Cv2.BitwiseOr(mask, relaxedHueMask, mask);

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
        Cv2.MorphologyEx(mask, morph, MorphTypes.Close, kernel, iterations: 2);
        Cv2.MorphologyEx(morph, morph, MorphTypes.Open, kernel, iterations: 1);
        morph.CopyTo(mask);
        return mask;
    }

    private static Mat BuildRedDominantMask(Mat source)
    {
        var channels = Cv2.Split(source);

        try
        {
            using var redMinusGreen = new Mat();
            using var redMinusBlue = new Mat();
            using var redDominatesGreen = new Mat();
            using var redDominatesBlue = new Mat();
            using var redBrightMask = new Mat();
            var mask = new Mat();

            Cv2.Subtract(channels[2], channels[1], redMinusGreen);
            Cv2.Subtract(channels[2], channels[0], redMinusBlue);
            Cv2.Compare(redMinusGreen, new Scalar(12), redDominatesGreen, CmpType.GT);
            Cv2.Compare(redMinusBlue, new Scalar(12), redDominatesBlue, CmpType.GT);
            Cv2.Compare(channels[2], new Scalar(90), redBrightMask, CmpType.GT);

            Cv2.BitwiseAnd(redDominatesGreen, redDominatesBlue, mask);
            Cv2.BitwiseAnd(mask, redBrightMask, mask);
            return mask;
        }
        finally
        {
            foreach (var channel in channels)
            {
                channel.Dispose();
            }
        }
    }

    private static Mat BuildNeutralSealMask(Mat source)
    {
        using var gray = new Mat();
        using var blur = new Mat();
        using var adaptive = new Mat();
        using var edges = new Mat();
        using var gradient = new Mat();
        using var texture = new Mat();
        using var merged = new Mat();
        var mask = new Mat();

        Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(gray, blur, new Size(5, 5), 0);
        Cv2.AdaptiveThreshold(blur, adaptive, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.BinaryInv, 31, 8);
        Cv2.Canny(blur, edges, 60, 180);
        Cv2.Laplacian(blur, gradient, MatType.CV_8U, 3);
        Cv2.Threshold(gradient, texture, 24, 255, ThresholdTypes.Binary);
        Cv2.BitwiseOr(adaptive, edges, merged);
        Cv2.BitwiseOr(merged, texture, mask);

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel, iterations: 2);
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, kernel, iterations: 1);
        Cv2.Dilate(mask, mask, kernel, iterations: 1);

        return mask;
    }

    private static IReadOnlyList<SealDetection> DetectFromMask(Mat source, Mat mask, String channelName, Boolean allowPartial)
    {
        Cv2.FindContours(mask, out var contours, out var hierarchy, RetrievalModes.Tree, ContourApproximationModes.ApproxSimple);
        var detections = new List<SealDetection>();
        var imageArea = source.Width * source.Height;
        var minimumSealSide = Math.Max(18f, Math.Min(source.Width, source.Height) * 0.018f);
        var isNeutralChannel = String.Equals(channelName, "opencv-neutral", StringComparison.Ordinal);

        for (var contourIndex = 0; contourIndex < contours.Length; contourIndex++)
        {
            if (hierarchy.Length > contourIndex && hierarchy[contourIndex].Parent >= 0)
                continue;

            var contour = contours[contourIndex];
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
            var touchesEdge = TouchesImageEdge(rect, source.Width, source.Height);
            var partialCandidate = allowPartial && touchesEdge;
            var minimumSide = Math.Min(rect.Width, rect.Height);
            var maximumSide = Math.Max(rect.Width, rect.Height);

            if (!partialCandidate && minimumSide < minimumSealSide)
                continue;

            if (partialCandidate && maximumSide < minimumSealSide)
                continue;

            if (!partialCandidate && circularity < 0.12 && (aspectRatio < 0.7f || aspectRatio > 1.4f))
                continue;

            if (partialCandidate && fillRatio < 0.06f)
                continue;

            if (String.Equals(channelName, "opencv-red", StringComparison.Ordinal) && !partialCandidate && (aspectRatio < 0.55f || aspectRatio > 1.8f))
                continue;

            using var roi = new Mat(source, rect);
            var edgeDensity = ComputeEdgeDensity(roi);
            var strokeDensity = ComputeStrokeDensity(mask, rect);
            if (edgeDensity < 0.015f && strokeDensity < 0.04f)
                continue;

            var redDominanceDensity = ComputeRedDominanceDensity(roi);
            if (String.Equals(channelName, "opencv-red", StringComparison.Ordinal) && redDominanceDensity < (partialCandidate ? 0.035f : 0.075f))
                continue;

            var saturationDensity = isNeutralChannel
                ? ComputeSaturationDensity(roi)
                : 0f;
            var grayscaleStructureScore = 0f;
            if (isNeutralChannel && saturationDensity < 0.015f)
            {
                var holeRatio = ComputeHoleRatio(contours, hierarchy, contourIndex, area);
                grayscaleStructureScore = ComputeGrayscaleSealScore(mask, rect, aspectRatio, circularity, fillRatio, edgeDensity, strokeDensity, holeRatio);
                var minimumGraySealScore = partialCandidate ? 0.42f : 0.5f;
                if (grayscaleStructureScore < minimumGraySealScore)
                    continue;
            }

            var score = 0.2f;
            score += MathF.Min(0.3f, MathF.Max(0f, (Single)circularity * 0.35f));
            score += MathF.Min(0.2f, fillRatio * 0.3f);
            score += MathF.Min(0.2f, edgeDensity * 1.4f);
            score += MathF.Min(0.2f, strokeDensity * 0.8f);
            score += MathF.Min(0.18f, redDominanceDensity * 1.6f);
            score += MathF.Min(0.15f, saturationDensity * 1.8f);
            score += MathF.Min(0.18f, grayscaleStructureScore * 0.18f);

            var promotedRedSupport = isNeutralChannel && !partialCandidate && redDominanceDensity >= 0.14f;
            if (promotedRedSupport)
                score = MathF.Max(score, 0.82f);

            if (partialCandidate)
                score = MathF.Max(score, 0.38f);

            var finalRect = partialCandidate && String.Equals(channelName, "opencv-red", StringComparison.Ordinal)
                ? ExpandPartialDetectionRect(rect, source.Width, source.Height)
                : rect;
            var detectionSource = promotedRedSupport
                ? "opencv-red-support"
                : grayscaleStructureScore > 0f ? "opencv-gray" : channelName;

            if (TrySplitMergedRedDetection(mask, finalRect, channelName, partialCandidate, minimumSealSide, out var splitRects))
            {
                foreach (var splitRect in splitRects)
                {
                    using var splitRoi = new Mat(source, splitRect);
                    var splitEdgeDensity = ComputeEdgeDensity(splitRoi);
                    var splitStrokeDensity = ComputeStrokeDensity(mask, splitRect);
                    var splitScore = MathF.Min(0.95f,
                        MathF.Max(score, 0.22f)
                        + MathF.Min(0.08f, splitEdgeDensity * 0.5f)
                        + MathF.Min(0.08f, splitStrokeDensity * 0.35f));
                    detections.Add(new SealDetection(splitRect, splitScore, 0, "seal", detectionSource));
                }

                continue;
            }

            detections.Add(new SealDetection(finalRect, MathF.Min(0.95f, score), 0, partialCandidate ? "partial-seal" : "seal", detectionSource));
        }

        return detections;
    }

    private static Boolean TrySplitMergedRedDetection(Mat mask, Rect rect, String channelName, Boolean partialCandidate, Single minimumSealSide, out IReadOnlyList<Rect> splitRects)
    {
        splitRects = [];
        if (partialCandidate || !String.Equals(channelName, "opencv-red", StringComparison.Ordinal))
            return false;

        var aspectRatio = rect.Width / (Single)Math.Max(1, rect.Height);
        if (aspectRatio < 1.45f)
            return false;

        using var roi = new Mat(mask, rect);
        var minimumProjection = Math.Max(3, (Int32)MathF.Ceiling(rect.Height * 0.08f));
        var minimumGapWidth = Math.Max(10, (Int32)MathF.Ceiling(rect.Width * 0.035f));
        var minimumSegmentWidth = Math.Max((Int32)MathF.Ceiling(minimumSealSide * 0.8f), (Int32)MathF.Ceiling(rect.Width * 0.18f));
        var segments = CollectProjectionSegments(roi, minimumProjection, minimumGapWidth, minimumSegmentWidth);
        if (segments.Count < 2)
            return false;

        var rects = new List<Rect>(segments.Count);
        foreach (var segment in segments)
        {
            var segmentRect = TryBuildSegmentRect(roi, rect, segment.Start, segment.EndExclusive, minimumSealSide);
            if (segmentRect == null)
                continue;

            rects.Add(segmentRect.Value);
        }

        if (rects.Count < 2)
            return false;

        var ordered = rects.OrderBy(item => item.Left).ToList();
        var totalWidth = ordered.Sum(item => item.Width);
        if (totalWidth < rect.Width * 0.45f)
            return false;

        splitRects = ordered;
        return true;
    }

    private static List<(Int32 Start, Int32 EndExclusive)> CollectProjectionSegments(Mat roi, Int32 minimumProjection, Int32 minimumGapWidth, Int32 minimumSegmentWidth)
    {
        var segments = new List<(Int32 Start, Int32 EndExclusive)>();
        var start = -1;
        var gapWidth = 0;

        for (var column = 0; column < roi.Width; column++)
        {
            var projection = Cv2.CountNonZero(roi.Col(column));
            var active = projection >= minimumProjection;

            if (active)
            {
                if (start < 0)
                    start = column;

                gapWidth = 0;
                continue;
            }

            if (start < 0)
                continue;

            gapWidth++;
            if (gapWidth < minimumGapWidth)
                continue;

            var endExclusive = column - gapWidth + 1;
            if (endExclusive - start >= minimumSegmentWidth)
                segments.Add((start, endExclusive));

            start = -1;
            gapWidth = 0;
        }

        if (start >= 0)
        {
            var endExclusive = roi.Width;
            if (endExclusive - start >= minimumSegmentWidth)
                segments.Add((start, endExclusive));
        }

        return segments;
    }

    private static Rect? TryBuildSegmentRect(Mat roi, Rect baseRect, Int32 startColumn, Int32 endExclusiveColumn, Single minimumSealSide)
    {
        if (endExclusiveColumn <= startColumn)
            return null;

        using var columnSlice = new Mat(roi, new Rect(startColumn, 0, endExclusiveColumn - startColumn, roi.Height));
        var left = -1;
        var right = -1;
        var top = -1;
        var bottom = -1;

        for (var column = 0; column < columnSlice.Width; column++)
        {
            if (Cv2.CountNonZero(columnSlice.Col(column)) == 0)
                continue;

            left = column;
            break;
        }

        if (left < 0)
            return null;

        for (var column = columnSlice.Width - 1; column >= left; column--)
        {
            if (Cv2.CountNonZero(columnSlice.Col(column)) == 0)
                continue;

            right = column;
            break;
        }

        for (var row = 0; row < columnSlice.Height; row++)
        {
            if (Cv2.CountNonZero(columnSlice.Row(row)) == 0)
                continue;

            top = row;
            break;
        }

        if (top < 0)
            return null;

        for (var row = columnSlice.Height - 1; row >= top; row--)
        {
            if (Cv2.CountNonZero(columnSlice.Row(row)) == 0)
                continue;

            bottom = row;
            break;
        }

        var localRect = new Rect(left, top, right - left + 1, bottom - top + 1);
        if (localRect.Width < minimumSealSide * 0.65f || localRect.Height < minimumSealSide * 0.65f)
            return null;

        var segmentRect = new Rect(
            baseRect.Left + startColumn + localRect.Left,
            baseRect.Top + localRect.Top,
            localRect.Width,
            localRect.Height);
        var aspectRatio = segmentRect.Width / (Single)Math.Max(1, segmentRect.Height);
        if (aspectRatio is < 0.3f or > 2.2f)
            return null;

        return segmentRect;
    }

    private static Rect ExpandPartialDetectionRect(Rect rect, Int32 imageWidth, Int32 imageHeight)
    {
        var touchesLeft = rect.Left <= 6;
        var touchesTop = rect.Top <= 6;
        var touchesRight = rect.Right >= imageWidth - 6;
        var touchesBottom = rect.Bottom >= imageHeight - 6;

        var targetSize = Math.Max(rect.Width, rect.Height) * 2.2f;
        Single expandedLeft = rect.Left;
        Single expandedTop = rect.Top;
        Single expandedRight = rect.Right;
        Single expandedBottom = rect.Bottom;

        if (touchesLeft)
        {
            expandedLeft = 0;
            expandedRight = Math.Max(expandedRight, targetSize);
        }
        else if (touchesRight)
        {
            expandedRight = imageWidth;
            expandedLeft = Math.Min(expandedLeft, imageWidth - targetSize);
        }
        else
        {
            var centerX = rect.Left + rect.Width / 2f;
            expandedLeft = centerX - targetSize / 2f;
            expandedRight = centerX + targetSize / 2f;
        }

        if (touchesTop)
        {
            expandedTop = 0;
            expandedBottom = Math.Max(expandedBottom, targetSize);
        }
        else if (touchesBottom)
        {
            expandedBottom = imageHeight;
            expandedTop = Math.Min(expandedTop, imageHeight - targetSize);
        }
        else
        {
            var centerY = rect.Top + rect.Height / 2f;
            expandedTop = centerY - targetSize / 2f;
            expandedBottom = centerY + targetSize / 2f;
        }

        var left = Math.Clamp((Int32)MathF.Floor(expandedLeft), 0, Math.Max(0, imageWidth - 1));
        var top = Math.Clamp((Int32)MathF.Floor(expandedTop), 0, Math.Max(0, imageHeight - 1));
        var right = Math.Clamp((Int32)MathF.Ceiling(expandedRight), left + 1, imageWidth);
        var bottom = Math.Clamp((Int32)MathF.Ceiling(expandedBottom), top + 1, imageHeight);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static Boolean TouchesImageEdge(Rect rect, Int32 width, Int32 height)
    {
        const Int32 tolerance = 6;
        return rect.Left <= tolerance || rect.Top <= tolerance || rect.Right >= width - tolerance || rect.Bottom >= height - tolerance;
    }

    private static Single ComputeEdgeDensity(Mat roi)
    {
        using var gray = new Mat();
        using var edges = new Mat();
        Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.Canny(gray, edges, 50, 150);
        return Cv2.CountNonZero(edges) / (Single)Math.Max(1, roi.Width * roi.Height);
    }

    private static Single ComputeStrokeDensity(Mat mask, Rect rect)
    {
        using var roi = new Mat(mask, rect);
        return Cv2.CountNonZero(roi) / (Single)Math.Max(1, rect.Width * rect.Height);
    }

    private static Single ComputeSaturationDensity(Mat roi)
    {
        using var hsv = new Mat();
        using var saturated = new Mat();
        Cv2.CvtColor(roi, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.InRange(hsv, new Scalar(0, 35, 0), new Scalar(180, 255, 255), saturated);
        return Cv2.CountNonZero(saturated) / (Single)Math.Max(1, roi.Width * roi.Height);
    }

    private static Single ComputeRedDominanceDensity(Mat roi)
    {
        var channels = Cv2.Split(roi);

        try
        {
            using var redMinusGreen = new Mat();
            using var redMinusBlue = new Mat();
            using var redDominatesGreen = new Mat();
            using var redDominatesBlue = new Mat();
            using var redBrightMask = new Mat();
            using var redMask = new Mat();

            Cv2.Subtract(channels[2], channels[1], redMinusGreen);
            Cv2.Subtract(channels[2], channels[0], redMinusBlue);
            Cv2.Compare(redMinusGreen, new Scalar(12), redDominatesGreen, CmpType.GT);
            Cv2.Compare(redMinusBlue, new Scalar(12), redDominatesBlue, CmpType.GT);
            Cv2.Compare(channels[2], new Scalar(90), redBrightMask, CmpType.GT);

            Cv2.BitwiseAnd(redDominatesGreen, redDominatesBlue, redMask);
            Cv2.BitwiseAnd(redMask, redBrightMask, redMask);
            return Cv2.CountNonZero(redMask) / (Single)Math.Max(1, roi.Width * roi.Height);
        }
        finally
        {
            foreach (var channel in channels)
            {
                channel.Dispose();
            }
        }
    }

    private static Single ComputeGrayscaleSealScore(Mat mask, Rect rect, Single aspectRatio, Double circularity, Single fillRatio, Single edgeDensity, Single strokeDensity, Single holeRatio)
    {
        using var roi = new Mat(mask, rect);
        var outerBandDensity = ComputeOuterBandDensity(roi);
        var centerDensity = ComputeCenterDensity(roi);
        var cornerDensity = ComputeCornerDensity(roi);
        var axisBalance = ComputeAxisBalance(roi);

        var aspectScore = 1f - Math.Clamp(Math.Abs(1f - aspectRatio) / 0.75f, 0f, 1f);
        var circularityScore = NormalizeScore((Single)circularity, 0.14f, 0.52f);
        var holeScore = NormalizeScore(holeRatio, 0.04f, 0.24f);
        var ringScore = NormalizeScore(outerBandDensity - centerDensity, 0.03f, 0.22f);
        var edgeScore = NormalizeScore(edgeDensity, 0.02f, 0.11f);
        var strokeScore = NormalizeScore(strokeDensity, 0.05f, 0.28f);
        var cornerPenalty = NormalizeScore(cornerDensity, 0.18f, 0.42f);
        var fillPenalty = NormalizeScore(fillRatio, 0.62f, 0.92f);

        var score = 0f;
        score += aspectScore * 0.2f;
        score += circularityScore * 0.2f;
        score += holeScore * 0.2f;
        score += ringScore * 0.16f;
        score += axisBalance * 0.12f;
        score += edgeScore * 0.06f;
        score += strokeScore * 0.06f;
        score -= cornerPenalty * 0.12f;
        score -= fillPenalty * 0.08f;

        if (cornerDensity >= outerBandDensity * 0.95f)
            score -= 0.08f;

        if (centerDensity >= outerBandDensity * 0.9f)
            score -= 0.08f;

        if (holeRatio <= 0.01f && circularity < 0.2)
            score -= 0.08f;

        return Math.Clamp(score, 0f, 1f);
    }

    private static Single ComputeHoleRatio(Point[][] contours, HierarchyIndex[] hierarchy, Int32 contourIndex, Double contourArea)
    {
        if (hierarchy.Length <= contourIndex || contourArea <= 0)
            return 0f;

        var childIndex = hierarchy[contourIndex].Child;
        if (childIndex < 0)
            return 0f;

        var childArea = 0d;
        while (childIndex >= 0)
        {
            childArea += Math.Abs(Cv2.ContourArea(contours[childIndex]));
            childIndex = hierarchy[childIndex].Next;
        }

        return Math.Clamp((Single)(childArea / contourArea), 0f, 1f);
    }

    private static Single ComputeOuterBandDensity(Mat roi)
    {
        var insetX = Math.Max(1, (Int32)MathF.Floor(roi.Width * 0.22f));
        var insetY = Math.Max(1, (Int32)MathF.Floor(roi.Height * 0.22f));
        if (roi.Width <= insetX * 2 || roi.Height <= insetY * 2)
            return Cv2.CountNonZero(roi) / (Single)Math.Max(1, roi.Width * roi.Height);

        using var inner = new Mat(roi, new Rect(insetX, insetY, roi.Width - insetX * 2, roi.Height - insetY * 2));
        var totalCount = Cv2.CountNonZero(roi);
        var innerCount = Cv2.CountNonZero(inner);
        var outerArea = roi.Width * roi.Height - inner.Width * inner.Height;
        return outerArea <= 0 ? 0f : (totalCount - innerCount) / (Single)outerArea;
    }

    private static Single ComputeCenterDensity(Mat roi)
    {
        var width = Math.Max(1, (Int32)MathF.Floor(roi.Width * 0.34f));
        var height = Math.Max(1, (Int32)MathF.Floor(roi.Height * 0.34f));
        var left = Math.Max(0, (roi.Width - width) / 2);
        var top = Math.Max(0, (roi.Height - height) / 2);
        using var center = new Mat(roi, new Rect(left, top, Math.Min(width, roi.Width - left), Math.Min(height, roi.Height - top)));
        return Cv2.CountNonZero(center) / (Single)Math.Max(1, center.Width * center.Height);
    }

    private static Single ComputeCornerDensity(Mat roi)
    {
        var width = Math.Max(1, (Int32)MathF.Floor(roi.Width * 0.22f));
        var height = Math.Max(1, (Int32)MathF.Floor(roi.Height * 0.22f));
        var cornerRects = new[]
        {
            new Rect(0, 0, width, height),
            new Rect(Math.Max(0, roi.Width - width), 0, width, height),
            new Rect(0, Math.Max(0, roi.Height - height), width, height),
            new Rect(Math.Max(0, roi.Width - width), Math.Max(0, roi.Height - height), width, height)
        };

        var active = 0;
        var area = 0;
        foreach (var cornerRect in cornerRects)
        {
            using var corner = new Mat(roi, cornerRect);
            active += Cv2.CountNonZero(corner);
            area += corner.Width * corner.Height;
        }

        return active / (Single)Math.Max(1, area);
    }

    private static Single ComputeAxisBalance(Mat roi)
    {
        var halfWidth = Math.Max(1, roi.Width / 2);
        var halfHeight = Math.Max(1, roi.Height / 2);
        using var left = new Mat(roi, new Rect(0, 0, halfWidth, roi.Height));
        using var right = new Mat(roi, new Rect(roi.Width - halfWidth, 0, halfWidth, roi.Height));
        using var top = new Mat(roi, new Rect(0, 0, roi.Width, halfHeight));
        using var bottom = new Mat(roi, new Rect(0, roi.Height - halfHeight, roi.Width, halfHeight));

        var leftDensity = Cv2.CountNonZero(left) / (Single)Math.Max(1, left.Width * left.Height);
        var rightDensity = Cv2.CountNonZero(right) / (Single)Math.Max(1, right.Width * right.Height);
        var topDensity = Cv2.CountNonZero(top) / (Single)Math.Max(1, top.Width * top.Height);
        var bottomDensity = Cv2.CountNonZero(bottom) / (Single)Math.Max(1, bottom.Width * bottom.Height);

        var horizontalBalance = 1f - Math.Min(1f, Math.Abs(leftDensity - rightDensity) / Math.Max(0.01f, Math.Max(leftDensity, rightDensity)));
        var verticalBalance = 1f - Math.Min(1f, Math.Abs(topDensity - bottomDensity) / Math.Max(0.01f, Math.Max(topDensity, bottomDensity)));
        return (horizontalBalance + verticalBalance) * 0.5f;
    }

    private static Single NormalizeScore(Single value, Single min, Single max)
    {
        if (max <= min)
            return value >= max ? 1f : 0f;

        return Math.Clamp((value - min) / (max - min), 0f, 1f);
    }

    private static List<SealDetection> FilterNeutralDetectionsNearRed(IReadOnlyList<SealDetection> redDetections, IReadOnlyList<SealDetection> neutralDetections)
    {
        var kept = new List<SealDetection>();

        foreach (var neutral in neutralDetections)
        {
            var relatedRedDetections = redDetections.Where(red => IsSupportingNeutralDetection(red.Box, neutral.Box)).ToList();
            if (relatedRedDetections.Count == 0)
                continue;

            if (relatedRedDetections.Count > 1)
                continue;

            var relatedRed = relatedRedDetections[0];
            var neutralArea = neutral.Box.Width * neutral.Box.Height;
            var redArea = relatedRed.Box.Width * relatedRed.Box.Height;
            if (neutralArea < redArea * 0.18f)
                continue;

            if (neutral.Box.Width > relatedRed.Box.Width * 1.45f || neutral.Box.Height > relatedRed.Box.Height * 1.45f)
                continue;

            kept.Add(neutral);
        }

        return kept;
    }

    private static Boolean IsNearRelatedDetection(Rect anchor, Rect candidate)
    {
        var union = Union(anchor, candidate);
        var maxWidth = Math.Max(anchor.Width, candidate.Width);
        var maxHeight = Math.Max(anchor.Height, candidate.Height);
        var overlap = ComputeRectIou(anchor, candidate);
        if (overlap > 0)
            return true;

        var centerDeltaX = Math.Abs((anchor.Left + anchor.Right) - (candidate.Left + candidate.Right)) / 2f;
        var centerDeltaY = Math.Abs((anchor.Top + anchor.Bottom) - (candidate.Top + candidate.Bottom)) / 2f;
        return centerDeltaX <= maxWidth * 0.9f && centerDeltaY <= maxHeight * 0.9f && union.Width <= maxWidth * 1.9f && union.Height <= maxHeight * 1.9f;
    }

    private static Boolean IsSupportingNeutralDetection(Rect redBox, Rect neutralBox)
    {
        if (ComputeRectIou(redBox, neutralBox) > 0.01f)
            return true;

        var expandedLeft = redBox.Left - Math.Max(12, (Int32)MathF.Ceiling(redBox.Width * 0.08f));
        var expandedTop = redBox.Top - Math.Max(12, (Int32)MathF.Ceiling(redBox.Height * 0.08f));
        var expandedRight = redBox.Right + Math.Max(12, (Int32)MathF.Ceiling(redBox.Width * 0.08f));
        var expandedBottom = redBox.Bottom + Math.Max(12, (Int32)MathF.Ceiling(redBox.Height * 0.08f));
        var centerX = (neutralBox.Left + neutralBox.Right) / 2f;
        var centerY = (neutralBox.Top + neutralBox.Bottom) / 2f;
        if (centerX < expandedLeft || centerX > expandedRight || centerY < expandedTop || centerY > expandedBottom)
            return false;

        var gapX = Math.Max(0, Math.Max(redBox.Left, neutralBox.Left) - Math.Min(redBox.Right, neutralBox.Right));
        var gapY = Math.Max(0, Math.Max(redBox.Top, neutralBox.Top) - Math.Min(redBox.Bottom, neutralBox.Bottom));
        return gapX <= Math.Max(12f, redBox.Width * 0.1f) && gapY <= Math.Max(12f, redBox.Height * 0.1f);
    }

    private static IReadOnlyList<SealDetection> MergeNearbyDetections(IReadOnlyList<SealDetection> detections, Int32 imageWidth, Int32 imageHeight)
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
                    if (!ShouldMerge(pending[firstIndex], pending[secondIndex], imageWidth, imageHeight))
                        continue;

                    pending[firstIndex] = MergeDetection(pending[firstIndex], pending[secondIndex], imageWidth, imageHeight);
                    pending.RemoveAt(secondIndex);
                    merged = true;
                    break;
                }
            }
        }

        return pending;
    }

    private static Boolean ShouldMerge(SealDetection first, SealDetection second, Int32 imageWidth, Int32 imageHeight)
    {
        if (ShouldKeepSeparateEdgePartials(first, second, imageWidth, imageHeight))
            return false;

        if (ShouldKeepSeparateDistinctSeals(first, second))
            return false;

        var union = Union(first.Box, second.Box);
        var maxWidth = Math.Max(first.Box.Width, second.Box.Width);
        var maxHeight = Math.Max(first.Box.Height, second.Box.Height);
        var gapX = Math.Max(0, Math.Max(first.Box.Left, second.Box.Left) - Math.Min(first.Box.Right, second.Box.Right));
        var gapY = Math.Max(0, Math.Max(first.Box.Top, second.Box.Top) - Math.Min(first.Box.Bottom, second.Box.Bottom));
        var centerDeltaX = Math.Abs((first.Box.Left + first.Box.Right) - (second.Box.Left + second.Box.Right)) / 2f;
        var centerDeltaY = Math.Abs((first.Box.Top + first.Box.Bottom) - (second.Box.Top + second.Box.Bottom)) / 2f;
        var closeEnough = gapX <= maxWidth * 0.6f && gapY <= maxHeight * 0.6f;
        var sameNeighborhood = centerDeltaX <= union.Width * 0.75f && centerDeltaY <= union.Height * 0.75f;
        var reasonableUnion = union.Width <= maxWidth * 1.8f && union.Height <= maxHeight * 1.8f;
        return closeEnough && sameNeighborhood && reasonableUnion;
    }

    private static Boolean ShouldKeepSeparateEdgePartials(SealDetection first, SealDetection second, Int32 imageWidth, Int32 imageHeight)
    {
        if (first.Label != "partial-seal" || second.Label != "partial-seal")
            return false;

        var touchesTop = first.Box.Top <= 6 && second.Box.Top <= 6;
        var touchesBottom = first.Box.Bottom >= imageHeight - 6 && second.Box.Bottom >= imageHeight - 6;
        var touchesLeft = first.Box.Left <= 6 && second.Box.Left <= 6;
        var touchesRight = first.Box.Right >= imageWidth - 6 && second.Box.Right >= imageWidth - 6;
        if (!touchesTop && !touchesBottom && !touchesLeft && !touchesRight)
            return false;

        var gapX = Math.Max(0, Math.Max(first.Box.Left, second.Box.Left) - Math.Min(first.Box.Right, second.Box.Right));
        var gapY = Math.Max(0, Math.Max(first.Box.Top, second.Box.Top) - Math.Min(first.Box.Bottom, second.Box.Bottom));
        var maxWidth = Math.Max(first.Box.Width, second.Box.Width);
        var maxHeight = Math.Max(first.Box.Height, second.Box.Height);

        if ((touchesTop || touchesBottom) && gapX > maxWidth * 0.35f)
            return true;

        if ((touchesLeft || touchesRight) && gapY > maxHeight * 0.35f)
            return true;

        return false;
    }

    private static Boolean ShouldKeepSeparateDistinctSeals(SealDetection first, SealDetection second)
    {
        if (first.Label == "partial-seal" || second.Label == "partial-seal")
            return false;

        if (ComputeRectIou(first.Box, second.Box) > 0.01f)
            return false;

        var gapX = Math.Max(0, Math.Max(first.Box.Left, second.Box.Left) - Math.Min(first.Box.Right, second.Box.Right));
        var gapY = Math.Max(0, Math.Max(first.Box.Top, second.Box.Top) - Math.Min(first.Box.Bottom, second.Box.Bottom));
        var maxWidth = Math.Max(first.Box.Width, second.Box.Width);
        var maxHeight = Math.Max(first.Box.Height, second.Box.Height);
        var centerDeltaX = Math.Abs((first.Box.Left + first.Box.Right) - (second.Box.Left + second.Box.Right)) / 2f;
        var centerDeltaY = Math.Abs((first.Box.Top + first.Box.Bottom) - (second.Box.Top + second.Box.Bottom)) / 2f;
        var horizontalGapThreshold = Math.Max(10f, Math.Min(first.Box.Width, second.Box.Width) * 0.08f);
        var verticalGapThreshold = Math.Max(10f, Math.Min(first.Box.Height, second.Box.Height) * 0.08f);

        if (gapX >= horizontalGapThreshold && centerDeltaY <= maxHeight * 0.45f)
            return true;

        if (gapY >= verticalGapThreshold && centerDeltaX <= maxWidth * 0.45f)
            return true;

        return false;
    }

    private static SealDetection MergeDetection(SealDetection first, SealDetection second, Int32 imageWidth, Int32 imageHeight)
    {
        var union = Union(first.Box, second.Box);
        var bounded = new Rect(
            Math.Max(0, union.X),
            Math.Max(0, union.Y),
            Math.Min(imageWidth - Math.Max(0, union.X), union.Width),
            Math.Min(imageHeight - Math.Max(0, union.Y), union.Height));
        var score = MathF.Max(first.Score, second.Score);
        var label = first.Label == "partial-seal" || second.Label == "partial-seal" ? "partial-seal" : "seal";
        var source = first.Source == second.Source ? first.Source : $"{first.Source}+{second.Source}";
        return new SealDetection(bounded, score, first.ClassId, label, source);
    }

    private static Rect Union(Rect first, Rect second)
    {
        var left = Math.Min(first.Left, second.Left);
        var top = Math.Min(first.Top, second.Top);
        var right = Math.Max(first.Right, second.Right);
        var bottom = Math.Max(first.Bottom, second.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static Single ComputeRectIou(Rect first, Rect second)
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
        return union <= 0 ? 0 : intersection / (Single)union;
    }
}

internal sealed class YoloOnnxSealDetector : IDisposable
{
    private readonly Microsoft.ML.OnnxRuntime.InferenceSession _session;
    private readonly StampDetectionOptions _options;
    private readonly String _inputName;

    public YoloOnnxSealDetector(StampDetectionOptions options)
    {
        _options = options;
        _session = new Microsoft.ML.OnnxRuntime.InferenceSession(options.ModelPath!);
        _inputName = _session.InputMetadata.Keys.First();
    }

    public IReadOnlyList<SealDetection> Detect(Mat source)
    {
        var (inputTensor, ratio, padX, padY) = Letterbox(source, _options.InputSize);
        var input = Microsoft.ML.OnnxRuntime.NamedOnnxValue.CreateFromTensor(_inputName, inputTensor);
        using var results = _session.Run([input]);
        var output = results.First().AsTensor<Single>();
        var detections = Decode(output, ratio, padX, padY, source.Width, source.Height, _options.Labels, _options.ConfidenceThreshold);
        return NonMaxSuppression.Apply(detections, _options.IouThreshold);
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    private static (Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<Single> Tensor, Single Ratio, Int32 PadX, Int32 PadY) Letterbox(Mat source, Int32 inputSize)
    {
        var ratio = Math.Min(inputSize / (Single)source.Width, inputSize / (Single)source.Height);
        var resizedWidth = Math.Max(1, (Int32)Math.Round(source.Width * ratio));
        var resizedHeight = Math.Max(1, (Int32)Math.Round(source.Height * ratio));
        var padX = (inputSize - resizedWidth) / 2;
        var padY = (inputSize - resizedHeight) / 2;

        using var resized = new Mat();
        using var letterbox = new Mat(new Size(inputSize, inputSize), MatType.CV_8UC3, new Scalar(114, 114, 114));
        Cv2.Resize(source, resized, new Size(resizedWidth, resizedHeight));
        resized.CopyTo(new Mat(letterbox, new Rect(padX, padY, resizedWidth, resizedHeight)));

        var tensor = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<Single>([1, 3, inputSize, inputSize]);

        for (var y = 0; y < inputSize; y++)
        {
            for (var x = 0; x < inputSize; x++)
            {
                var pixel = letterbox.At<Vec3b>(y, x);
                tensor[0, 0, y, x] = pixel.Item2 / 255f;
                tensor[0, 1, y, x] = pixel.Item1 / 255f;
                tensor[0, 2, y, x] = pixel.Item0 / 255f;
            }
        }

        return (tensor, ratio, padX, padY);
    }

    private static List<SealDetection> Decode(
        Microsoft.ML.OnnxRuntime.Tensors.Tensor<Single> output,
        Single ratio,
        Int32 padX,
        Int32 padY,
        Int32 originalWidth,
        Int32 originalHeight,
        IReadOnlyList<String> labels,
        Single confidenceThreshold)
    {
        var dimensions = output.Dimensions.ToArray();
        if (dimensions.Length != 3)
            throw new NotSupportedException($"当前 Demo 仅支持三维 YOLO 输出，实际输出维度为 {dimensions.Length}");

        var classCount = labels.Count;
        var detections = new List<SealDetection>();

        if (dimensions[1] >= 6 && dimensions[2] > dimensions[1])
        {
            var featureCount = dimensions[1];
            var boxCount = dimensions[2];
            for (var index = 0; index < boxCount; index++)
            {
                var centerX = output[0, 0, index];
                var centerY = output[0, 1, index];
                var width = output[0, 2, index];
                var height = output[0, 3, index];
                AppendDetection(output, labels, confidenceThreshold, detections, centerX, centerY, width, height, featureCount, index, ratio, padX, padY, originalWidth, originalHeight);
            }
        }
        else
        {
            var boxCount = dimensions[1];
            var featureCount = dimensions[2];
            for (var index = 0; index < boxCount; index++)
            {
                var centerX = output[0, index, 0];
                var centerY = output[0, index, 1];
                var width = output[0, index, 2];
                var height = output[0, index, 3];
                AppendDetection(output, labels, confidenceThreshold, detections, centerX, centerY, width, height, featureCount, index, ratio, padX, padY, originalWidth, originalHeight, channelLast: true);
            }
        }

        return detections;
    }

    private static void AppendDetection(
        Microsoft.ML.OnnxRuntime.Tensors.Tensor<Single> output,
        IReadOnlyList<String> labels,
        Single confidenceThreshold,
        List<SealDetection> detections,
        Single centerX,
        Single centerY,
        Single width,
        Single height,
        Int32 featureCount,
        Int32 index,
        Single ratio,
        Int32 padX,
        Int32 padY,
        Int32 originalWidth,
        Int32 originalHeight,
        Boolean channelLast = false)
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

        var left = ClampToInt(x1, 0, originalWidth - 1);
        var top = ClampToInt(y1, 0, originalHeight - 1);
        var right = ClampToInt(x2, left + 1, originalWidth);
        var bottom = ClampToInt(y2, top + 1, originalHeight);
        var label = bestClass < labels.Count ? labels[bestClass] : $"class_{bestClass}";
        detections.Add(new SealDetection(new Rect(left, top, right - left, bottom - top), bestScore, bestClass, label, "onnx"));
    }

    private static Int32 ClampToInt(Single value, Int32 min, Int32 max)
    {
        var rounded = (Int32)Math.Round(value);
        if (rounded < min)
            return min;
        if (rounded > max)
            return max;
        return rounded;
    }
}

internal static class NonMaxSuppression
{
    public static IReadOnlyList<SealDetection> Apply(IReadOnlyList<SealDetection> detections, Single iouThreshold)
    {
        if (detections.Count <= 1)
            return detections;

        var ordered = detections.OrderByDescending(item => item.Score).ToList();
        var kept = new List<SealDetection>();

        while (ordered.Count > 0)
        {
            var current = ordered[0];
            kept.Add(current);
            ordered.RemoveAt(0);
            ordered.RemoveAll(candidate => ComputeIou(current.Box, candidate.Box) >= iouThreshold);
        }

        return kept;
    }

    private static Single ComputeIou(Rect first, Rect second)
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
        return union <= 0 ? 0 : intersection / (Single)union;
    }
}

internal sealed record PageImage(Int32 PageNumber, Mat Image);

internal sealed record SealDetection(Rect Box, Single Score, Int32 ClassId, String Label, String Source);

internal sealed record StampPageResult(Int32 PageNumber, Int32 OpenCvCount, Int32 OnnxCount, Int32 FinalCount, String AnnotatedImagePath);

internal sealed class StampDetectionRunResult
{
    public StampDetectionRunResult(IReadOnlyList<StampPageResult> pages, String outputDirectory, String? warning)
    {
        Pages = pages;
        OutputDirectory = outputDirectory;
        Warning = warning;
        TotalCount = pages.Sum(item => item.FinalCount);
    }

    public IReadOnlyList<StampPageResult> Pages { get; }

    public String OutputDirectory { get; }

    public Int32 TotalCount { get; }

    public String? Warning { get; }
}