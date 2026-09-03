using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace BimS
{
    internal static class Program
    {
        private const string ApiUrl = "https://api.openai.com/v1/responses";
        private const string Model = "gpt-4.1-mini";

        private const string AnalysisRequest = @"BIM-S выполнил AI-проверку двух версий одной Revit-модели исходной и полученной после запуска плагина.
Проверка проводилась на техническом уровне по следующим данным:

1. Из обеих моделей Revit
   Идентификаторы элементов Element ID, Unique ID;
   Типовые, Shared и системные параметры элементов;
   Геометрия элементов, их положение, размеры, объёмы, 3D-координаты и ориентация;
   Структурные связи и зависимости между элементами;
   Метаданные модели версия, дата и время сохранения, автор изменений, ссылки на подключаемые файлы.
2. Из плагина
   Лог изменений;
   Список элементов, к которым применялись изменения;
   Вид и характер изменений;
   Цели изменений;
   Параметры и правила, по которым работал плагин;
   Ошибки и предупреждения, возникшие во время его работы.

По результатам проверки BIM-S обнаружил неожиданное изменение - после работы плагина изменилась арматура лестничной площадки, хотя это изменение не относилось к ожидаемой области работы плагина.
Сформулируй результат для BIM-координатора, хорошо работающего в Revit, но не являющегося разработчиком Revit.
Объясни
Как именно в модели изменились элементы, на которые плагин вообще-то не должен был повлиять;
В результате изменения каких параметров этих элементов что-то произошло;
Что конкретно BIM-координатор может сделать в модели Revit перед запуском плагина, чтобы его запуск потом не повлиял на элементы, которые, как уже выявлено предыдущим запросом, были случайно задеты и изменены;
Что конкретно BIM-координатор может сделать в самом плагине перед его запуском, чтобы он не повлиял на элементы, которые, как уже выявлено предыдущим запросом, были случайно задеты и изменены.";

        private static void Main()
        {
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            try
            {
                RunAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Ошибка: " + ex.Message);
                Environment.ExitCode = 1;
            }
        }

        private static async Task RunAsync()
        {
            Console.WriteLine("1 — выполнить запрос с temperature = 0");
            Console.WriteLine("2 — выполнить запрос с temperature = 0.7");
            Console.WriteLine("3 — выполнить запрос с temperature = 1.2");
            Console.Write("Выберите вариант: ");

            double temperature = GetTemperature(Console.ReadLine());

            string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Переменная среды OPENAI_API_KEY не задана.");

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(5);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                Console.WriteLine(await AskAsync(client, AnalysisRequest, temperature));
            }
        }

        private static double GetTemperature(string choice)
        {
            switch (choice)
            {
                case "1": return 0.0;
                case "2": return 0.7;
                case "3": return 1.2;
                default: throw new InvalidOperationException("Введите число от 1 до 3.");
            }
        }

        private static async Task<string> AskAsync(HttpClient client, string prompt, double temperature)
        {
            var body = new Dictionary<string, object>
            {
                ["model"] = Model,
                ["input"] = prompt,
                ["temperature"] = temperature,
                ["store"] = false
            };

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            using (var content = new StringContent(serializer.Serialize(body), Encoding.UTF8, "application/json"))
            using (HttpResponseMessage response = await client.PostAsync(ApiUrl, content))
            {
                string json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        "OpenAI API вернул " + (int)response.StatusCode + " " + response.ReasonPhrase + ": " +
                        ExtractApiError(json));
                }

                return ExtractOutputText(json);
            }
        }

        private static string ExtractOutputText(string json)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            var result = new StringBuilder();

            if (root == null)
                throw new InvalidOperationException("OpenAI API вернул JSON неожиданного формата.");

            if (root.TryGetValue("output", out object outputObject))
            {
                foreach (object itemObject in EnumerateArray(outputObject))
                {
                    var item = itemObject as Dictionary<string, object>;
                    if (item == null || !item.TryGetValue("content", out object contentObject))
                        continue;

                    foreach (object partObject in EnumerateArray(contentObject))
                    {
                        var part = partObject as Dictionary<string, object>;
                        if (part != null &&
                            part.TryGetValue("type", out object type) &&
                            string.Equals(type as string, "output_text", StringComparison.Ordinal) &&
                            part.TryGetValue("text", out object text) &&
                            text is string partText)
                        {
                            result.Append(partText);
                        }
                    }
                }
            }

            if (result.Length == 0)
            {
                string status = root.TryGetValue("status", out object value) ? value as string : null;
                throw new InvalidOperationException(
                    "В ответе OpenAI API отсутствует текст результата (status: " +
                    (status ?? "не указан") + ").");
            }

            return result.ToString();
        }

        private static IEnumerable EnumerateArray(object value)
        {
            return value is IEnumerable items && !(value is string) ? items : Array.Empty<object>();
        }

        private static string ExtractApiError(string json)
        {
            try
            {
                var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                if (root != null &&
                    root.TryGetValue("error", out object errorObject) &&
                    errorObject is Dictionary<string, object> error &&
                    error.TryGetValue("message", out object message) &&
                    message is string messageText)
                {
                    return messageText;
                }
            }
            catch
            {
                // Возвращаем безопасное общее сообщение ниже.
            }

            return "подробности ответа не распознаны";
        }
    }
}
