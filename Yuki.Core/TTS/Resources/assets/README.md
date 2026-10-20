# Ресурсы для TTS (Supertonic 3)
Данная папка содержит вспомогательные файлы конфигурации. Сами ONNX-модели и голосовые стили не хранятся в репозитории из-за ограничений GitHub на размер файлов.

1. Перейдите в раздел **[Releases](https://github.com/riwvv/Yuki/releases)** .
2. Скачайте архив `assets.zip`.
3. Распакуйте архив **в текущую папку** (`Yuki.Core/TTS/Resources/assets/`).

Ожидаемая структура после распаковки

```text
assets/
├── onnx/
│   ├── duration_predictor.onnx
│   ├── text_encoder.onnx
│   ├── vector_estimator.onnx
│   ├── vocoder.onnx
│   ├── tts.json
│   └── unicode_indexer.json
└── voice_styles/
    └── F5.json
