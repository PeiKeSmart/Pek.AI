# 印章标注工具

这个项目是一个 C# WinForms 标注工具，用来给印章检测模型准备训练数据。

它解决的是“训练数据标注”问题，不是给 ONNX 本身定义一种新格式。当前工具导出的核心格式是 YOLO 标签，同时会输出一份 `labels.txt`，用于让当前 ONNX 推理 Demo 与训练时的类别顺序保持一致。

## 支持的输出

1. 每张图片对应一个 YOLO 标签文件：`xxx.txt`
2. `labels.txt`：每行一个类别名
3. `dataset.yaml`：YOLO 训练数据描述文件
4. `annotations.json`：标注清单

## 使用方式

1. 启动项目：

```powershell
dotnet run --project .\Pek.AI.StampAnnotationTool\Pek.AI.StampAnnotationTool.csproj
```

2. 打开图片目录
3. 设置类别列表，例如：`seal,partial-seal`
4. 左键拖拽画框，右键点框删除
5. 点击“保存当前”或“全部保存”
6. 点击“导出 YOLO 数据集”生成训练目录

## 与当前 ONNX Demo 的配合

当前检测 Demo 已支持 `--labels-file` 参数，可以直接读取本工具导出的 `labels.txt`：

```powershell
dotnet run --project .\Pek.AI.StampDetectionDemo\Pek.AI.StampDetectionDemo.csproj -- --input D:\sample\seal.png --model D:\model\best.onnx --labels-file D:\dataset\labels.txt
```

这样可以保证训练时类别顺序与推理时类别顺序一致。