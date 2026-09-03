# BIM-S

Консольное приложение C# для .NET Framework 4.8 отправляет в OpenAI Responses API один фиксированный запрос о неожиданном изменении арматуры лестничной площадки в Revit-модели.

Перед отправкой пользователь выбирает один из трёх вариантов:

1. `temperature = 0`
2. `temperature = 0.7`
3. `temperature = 1.2`

Выполняется только один API-запрос с выбранным значением. Текст запроса во всех вариантах одинаков.

## Запуск

1. Установите .NET Framework 4.8 Developer Pack и Visual Studio/MSBuild.
2. Задайте API-ключ только в переменной среды текущего процесса PowerShell:

   ```powershell
   $env:OPENAI_API_KEY = "ваш-ключ"
   ```

3. Соберите и запустите приложение:

   ```powershell
   dotnet build .\BIM-S.sln
   .\bin\Debug\net48\BIM-S.exe
   ```

Приложение не сохраняет и не выводит API-ключ.
