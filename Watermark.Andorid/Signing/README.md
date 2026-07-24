# Android 发布签名

`Watermark.Release.keystore`、`Watermark.Release.keyInfo` 与 `AndroidSigning.props` 仅保留在本机，均被 Git 忽略。

Release 打包脚本会加载 `AndroidSigning.props` 并使用其中的正式签名配置。首次配置时，将 `AndroidSigning.props.example` 复制为 `AndroidSigning.props`，再填写本机 keystore 与密钥口令。不得将口令写入项目文件、文档或提交记录。
