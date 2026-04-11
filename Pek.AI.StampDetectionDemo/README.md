# 印章计数 Demo

这个 Demo 用于验证下面这条链路：

1. 输入图片或 PDF
2. 使用 OpenCvSharp 提取红色印章候选区域
3. 可选使用 YOLO ONNX 模型进行目标检测
4. 输出每页印章数量与标注图

## 运行方式

仅使用 OpenCV 候选计数：

```powershell
dotnet run --project Pek.AI.StampDetectionDemo -- --input D:\sample\seal.jpg
```

叠加 YOLO ONNX 检测：

```powershell
dotnet run --project Pek.AI.StampDetectionDemo -- --input D:\sample\seal.pdf --model D:\model\seal.onnx --labels seal
```

## 参数说明

- `--input`：必填，图片或 PDF 路径
- `--model`：可选，YOLO 导出的 ONNX 模型路径
- `--output`：可选，输出目录
- `--conf`：可选，置信度阈值，默认 `0.25`
- `--iou`：可选，NMS 阈值，默认 `0.45`
- `--size`：可选，模型输入尺寸，默认 `640`
- `--labels`：可选，类别名称，逗号分隔，默认 `seal`

## 输出内容

- 控制台会输出每页的 OpenCV 候选数、ONNX 检测数、最终计数
- 输出目录会生成 `page_001.png` 这类标注图

## 说明

- 没有提供模型时，最终计数直接采用 OpenCV 候选结果
- 提供模型后，最终计数优先采用 ONNX 检测结果
- 这个 Demo 默认偏向识别红色圆章、椭圆章，同时补充了实验性的灰章结构检测；复杂的黑白扫描章、电子章场景仍建议训练专门的 YOLO 模型兜底

## 模型训练

仓库里已经补充了训练模板，可直接参考：

- `Doc/印章检测模型训练说明.md`
- `Pek.AI.StampDetectionDemo/Training/stamp-seal.data.yaml`
- `Pek.AI.StampDetectionDemo/Training/train_yolo.py`

建议做法是：

1. 先采集并标注你的业务样本
2. 使用 YOLO 训练检测模型
3. 导出 ONNX
4. 再用当前 Demo 进行推理计数验证