# BIM-S

Учебное консольное приложение C# для .NET Framework 4.8. Оно отправляет один и тот же виртуальный снимок состояния BIM-модели в OpenAI Responses API тремя способами: simple prompting, system prompt и multi-perspective prompting.

## Запуск

1. Установите .NET Framework 4.8 Developer Pack и Visual Studio/MSBuild.
2. Задайте ключ только в переменной среды текущего процесса PowerShell:

   ```powershell
   $env:OPENAI_API_KEY = "ваш-ключ"
   ```

3. Соберите и запустите:

   ```powershell
   dotnet build .\BIM-S.sln
   .\bin\Debug\net48\BIM-S.exe
   ```

Приложение не сохраняет и не выводит API-ключ. Для всех запросов используется одинаковый лимит `max_output_tokens = 700`; ответы печатаются отдельно с заголовками.
