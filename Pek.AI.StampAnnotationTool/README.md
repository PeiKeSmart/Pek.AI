# 印章标注工具

这个项目是一个 C# WinForms 标注工具，用来给印章检测模型准备训练数据。

它解决的是“训练数据标注”问题，不是给 ONNX 本身定义一种新格式。当前工具导出的核心格式是 YOLO 标签，同时会输出一份 `labels.txt`，用于让当前 ONNX 推理 Demo 与训练时的类别顺序保持一致。

## 支持的输出

1. 每张图片对应一个 YOLO 标签文件，默认保存在图片目录下的 `labels/xxx.txt`
2. `labels.txt`：每行一个类别名
3. `dataset.yaml`：YOLO 训练数据描述文件
4. `annotations.json`：标注清单

说明：当前工具统一使用 `labels/xxx.txt` 布局，不再读取旧的“图片同目录 txt”布局。

## 使用方式

1. 启动项目：

```powershell
dotnet run --project .\Pek.AI.StampAnnotationTool\Pek.AI.StampAnnotationTool.csproj
```

2. 打开图片目录
3. 设置类别列表，例如：`seal,partial-seal`
4. 如果你已经训练并导出了 ONNX 模型，可以在“预标注模型”里选择模型文件
5. 左键拖拽画框，右键点框删除
6. 左键点击已有框可选中并拖动
7. 数字键 `1-9` 切换当前类别，并会对选中框直接改类别
8. 方向键微调选中框位置，`Shift + 方向键` 调整选中框大小
9. 鼠标滚轮缩放，按住中键拖动画布平移，双击画布重置视图
10. 可以点击“自动预标注当前”或“自动预标注未标注”，先生成候选框再人工修正
11. 默认开启“自动保存”，切换图片时会自动保存当前图片的标注
12. `Delete` 删除最后一个框，`PageUp/PageDown` 切换图片，`Ctrl+S` 保存
13. 点击“保存当前”或“全部保存”
14. 点击“导出 YOLO 数据集”生成训练目录
15. 点击“导出轻量标签”只导出标签文件、`labels.txt` 和 `annotations.json`
16. 点击“训练并导出 ONNX”可以直接调用仓库内的 YOLO 训练脚本，自动导出 `best.onnx`

## 与当前 ONNX Demo 的配合

当前检测 Demo 已支持 `--labels-file` 参数，可以直接读取本工具导出的 `labels.txt`：

```powershell
dotnet run --project .\Pek.AI.StampDetectionDemo\Pek.AI.StampDetectionDemo.csproj -- --input D:\sample\seal.png --model D:\model\best.onnx --labels-file D:\dataset\labels.txt
```

这样可以保证训练时类别顺序与推理时类别顺序一致。

## 训练与导出模型

当前标注工具已经内置“训练并导出 ONNX”入口，流程是：

1. 先自动导出当前图片目录下的 `yolo/` 数据集
2. 调用仓库中的 `Pek.AI.StampDetectionDemo/Training/train_yolo.py`
3. 训练完成后自动导出 `best.onnx`
4. 可选自动把导出的 `best.onnx` 回填到“预标注模型”输入框中

前置要求：

1. 本机已安装 Python
2. 已安装 `ultralytics`

安装示例：

```powershell
pip install ultralytics
```

默认参数可以在界面中修改，包括：

1. 基础模型，例如 `yolov8n.pt`
2. `epochs`
3. `batch`
4. 图像尺寸 `imgsz`
5. 训练设备 `device`，例如 `cpu`、`0`

## 自动预标注说明

当前工具内置了一套和现有检测 Demo 同思路的启发式预标注逻辑：

1. 红章候选通道
2. 非红章候选通道
3. 半章/残章放宽判断
4. 候选去重与邻近合并

它的定位是“先出初始框，减少手工框选时间”，不是最终训练标签。导出的训练数据仍建议人工复核一遍，尤其是复杂背景、电子章、浅色章和骑缝章场景。

如果你已经训练并导出了 ONNX 模型，工具现在会优先使用这个模型做预标注；只有在未配置模型、模型未检出结果或模型执行失败时，才会回退到内置启发式预标注。

## 轻量导出说明

“导出轻量标签”不会复制原始图片，只会导出：

1. `labels/` 目录下的标签文件
2. `labels.txt`
3. `annotations.json`

适合图片已经由其他流程统一管理、你只想单独打包标签结果的场景。