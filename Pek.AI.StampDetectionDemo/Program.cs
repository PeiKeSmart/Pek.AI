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
            Cv2.Rectangle(image, detection.Box, color, 2);
            var caption = $"{prefix}:{detection.Score:0.00}";
            var textPoint = new Point(detection.Box.X, Math.Max(18, detection.Box.Y - 6));
            Cv2.PutText(image, caption, textPoint, HersheyFonts.HersheySimplex, 0.6, color, 2);
        }
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
    public IReadOnlyList<SealDetection> Detect(Mat source)
    {
        using var redMask = BuildRedSealMask(source);
        var redDetections = DetectFromMask(source, redMask, channelName: "opencv-red", allowPartial: true);

        using var neutralMask = BuildNeutralSealMask(source);
        var neutralDetections = DetectFromMask(source, neutralMask, channelName: "opencv-neutral", allowPartial: true);

        if (redDetections.Count > 0)
            neutralDetections = FilterNeutralDetectionsNearRed(redDetections, neutralDetections);

        var detections = new List<SealDetection>(redDetections.Count + neutralDetections.Count);
        detections.AddRange(redDetections);
        detections.AddRange(neutralDetections);

        var filtered = NonMaxSuppression.Apply(detections, 0.3f);
        return MergeNearbyDetections(filtered, source.Width, source.Height);
    }

    private static Mat BuildRedSealMask(Mat source)
    {
        using var hsv = new Mat();
        using var mask1 = new Mat();
        using var mask2 = new Mat();
        var mask = new Mat();
        using var morph = new Mat();

        Cv2.CvtColor(source, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.InRange(hsv, new Scalar(0, 80, 60), new Scalar(15, 255, 255), mask1);
        Cv2.InRange(hsv, new Scalar(160, 80, 60), new Scalar(180, 255, 255), mask2);
        Cv2.BitwiseOr(mask1, mask2, mask);

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
        Cv2.MorphologyEx(mask, morph, MorphTypes.Close, kernel, iterations: 2);
        Cv2.MorphologyEx(morph, morph, MorphTypes.Open, kernel, iterations: 1);
        morph.CopyTo(mask);
        return mask;
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
        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var detections = new List<SealDetection>();
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
            var touchesEdge = TouchesImageEdge(rect, source.Width, source.Height);
            var partialCandidate = allowPartial && (touchesEdge || circularity < 0.22 || fillRatio < 0.2f);

            if (!partialCandidate && circularity < 0.12 && (aspectRatio < 0.7f || aspectRatio > 1.4f))
                continue;

            if (partialCandidate && fillRatio < 0.06f)
                continue;

            var roi = new Mat(source, rect);
            var edgeDensity = ComputeEdgeDensity(roi);
            var strokeDensity = ComputeStrokeDensity(mask, rect);
            if (edgeDensity < 0.015f && strokeDensity < 0.04f)
                continue;

            var score = 0.2f;
            score += MathF.Min(0.3f, MathF.Max(0f, (Single)circularity * 0.35f));
            score += MathF.Min(0.2f, fillRatio * 0.3f);
            score += MathF.Min(0.2f, edgeDensity * 1.4f);
            score += MathF.Min(0.2f, strokeDensity * 0.8f);
            if (partialCandidate)
                score = MathF.Max(score, 0.38f);

            var finalRect = partialCandidate && String.Equals(channelName, "opencv-red", StringComparison.Ordinal)
                ? ExpandPartialDetectionRect(rect, source.Width, source.Height)
                : rect;
            detections.Add(new SealDetection(finalRect, MathF.Min(0.95f, score), 0, partialCandidate ? "partial-seal" : "seal", channelName));
        }

        return detections;
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

    private static List<SealDetection> FilterNeutralDetectionsNearRed(IReadOnlyList<SealDetection> redDetections, IReadOnlyList<SealDetection> neutralDetections)
    {
        var kept = new List<SealDetection>();

        foreach (var neutral in neutralDetections)
        {
            var relatedRedDetections = redDetections.Where(red => IsNearRelatedDetection(red.Box, neutral.Box)).ToList();
            if (relatedRedDetections.Count == 0)
                continue;

            if (relatedRedDetections.Count > 1)
                continue;

            var relatedRed = relatedRedDetections[0];
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