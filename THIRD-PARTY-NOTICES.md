# 第三方声明（Third-Party Notices）

LanFlow Windows 客户端（`native/LanFlow.Desktop`）的截图标注与贴图窗在交互流程、视觉样式上参考了下列开源项目：

## dsh-screenshot

- 项目：`dsh-screenshot`
- 版权：Copyright (c) 2026 秋晏笙
- 许可：MIT License
- 用途说明：仅参考其框选 / 标注两阶段交互、工具条布局、放大镜与贴图窗行为；LanFlow 中的实现为使用 C# / WPF 独立编写，未复制其 PowerShell + WinForms 源码。

```
MIT License

Copyright (c) 2026 秋晏笙

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## 可选 OCR 组件（`LanFlow-{version}-ocr.zip`）

OCR 组件不随主程序分发，仅在用户于设置页手动开启并同意下载后，由独立的 bundle 安装到 `%APPDATA%\LanFlow\ocr`。该 bundle 包含以下第三方内容：

### ONNX Runtime

- 项目：`microsoft/onnxruntime`
- 版权：Copyright (c) Microsoft Corporation
- 许可：MIT License
- 用途说明：CPU 推理运行时（`Microsoft.ML.OnnxRuntime` 1.20.1、`onnxruntime.dll`、`onnxruntime_providers_shared.dll`），未做修改。

### PaddleOCR PP-OCRv5 mobile 模型

- 项目：`PaddlePaddle/PaddleOCR`
- 许可：Apache License 2.0
- 用途说明：使用官方发布的 `PP-OCRv5_mobile_det` / `PP-OCRv5_mobile_rec` ONNX 模型与 `ppocrv5_dict.txt` 字典，未做修改。

### Microsoft Visual C++ 可再分发组件

- 文件：`vcruntime140.dll`、`vcruntime140_1.dll`、`msvcp140.dll`、`msvcp140_1.dll`
- 许可：Microsoft Visual C++ Redistributable 许可条款（可再分发组件）
- 用途说明：供未安装 VC++ 运行库的机器加载 ONNX Runtime 使用；来源为 Windows 系统目录中的同版本可再分发 DLL。

Apache License 2.0 全文见 <https://www.apache.org/licenses/LICENSE-2.0>；MIT 许可全文同上文第一节。

