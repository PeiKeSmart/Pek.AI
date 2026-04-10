import argparse
from ultralytics import YOLO


def main() -> None:
    parser = argparse.ArgumentParser(description="Train a YOLO model for stamp detection.")
    parser.add_argument("--data", required=True, help="Dataset yaml path.")
    parser.add_argument("--model", default="yolov8n.pt", help="Base YOLO model.")
    parser.add_argument("--imgsz", type=int, default=640, help="Training image size.")
    parser.add_argument("--epochs", type=int, default=100, help="Training epochs.")
    parser.add_argument("--batch", type=int, default=16, help="Batch size.")
    parser.add_argument("--device", default="0", help="CUDA device id, cpu, or mps.")
    parser.add_argument("--project", default="runs/detect", help="Output project directory.")
    parser.add_argument("--name", default="stamp-demo", help="Run name.")
    args = parser.parse_args()

    model = YOLO(args.model)
    model.train(
        data=args.data,
        imgsz=args.imgsz,
        epochs=args.epochs,
        batch=args.batch,
        device=args.device,
        project=args.project,
        name=args.name,
        close_mosaic=10,
        degrees=10,
        translate=0.05,
        scale=0.15,
        fliplr=0.0,
        hsv_h=0.01,
        hsv_s=0.3,
        hsv_v=0.2,
    )

    best_model = f"{args.project}/{args.name}/weights/best.pt"
    export_model = YOLO(best_model)
    export_model.export(format="onnx", imgsz=args.imgsz, opset=12, simplify=True)


if __name__ == "__main__":
    main()