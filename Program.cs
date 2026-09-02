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

        private const string Request1 = "Разработай алгоритм автоматической проверки двух версий одной Revit-модели до и после запуска плагина. Определи, какие данные необходимо получить из моделей и плагина, что необходимо сравнить между версиями и как определить все произошедшие изменения элементов, разделив их на ожидаемые и неожиданные.";
        private const string Request2 = "Решай пошагово.\nРазработай алгоритм автоматической проверки двух версий одной Revit-модели до и после запуска плагина. Сначала определи, какие данные нужно получить из моделей и плагина, затем — как сопоставить элементы, потом — что именно сравнивать, и в конце — как разделить найденные изменения на ожидаемые и неожиданные.";
        private const string PromptGenerationRequest = "Составь лучший промпт для решения следующей задачи: разработать алгоритм автоматической проверки двух версий одной Revit-модели до и после запуска плагина, определить, какие данные нужно получить из моделей и плагина, что сравнивать и как разделить изменения на ожидаемые и неожиданные. Верни только готовый промпт, без вступления, пояснений и кавычек.";
        private const string Request4 = "Разработай алгоритм автоматической проверки двух версий одной Revit-модели до и после запуска плагина.\nРеши эту задачу с позиции трёх экспертов: BIM-координатора по Revit, BIM-программиста по Revit и разработчика программы Revit. Сначала приведи отдельное решение каждого эксперта в самостоятельном разделе, не смешивая их роли и выводы. Затем объедини их выводы в один итоговый алгоритм в отдельном разделе.";

        private static void Main()
        {
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            try { RunAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { Console.Error.WriteLine("Ошибка: " + ex.Message); Environment.ExitCode = 1; }
        }

        private static async Task RunAsync()
        {
            Console.WriteLine("1 — Запрос №1");
            Console.WriteLine("2 — Запрос №2");
            Console.WriteLine("3 — Запрос №3");
            Console.WriteLine("4 — Запрос №4");
            Console.Write("Выберите режим: ");
            string choice = Console.ReadLine();
            if (choice != "1" && choice != "2" && choice != "3" && choice != "4")
                throw new InvalidOperationException("Введите число от 1 до 4.");

            string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Переменная среды OPENAI_API_KEY не задана.");

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(5);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                switch (choice)
                {
                    case "1": Console.WriteLine(await AskAsync(client, Request1)); break;
                    case "2": Console.WriteLine(await AskAsync(client, Request2)); break;
                    case "3": await RunTwoStepRequestAsync(client); break;
                    case "4": Console.WriteLine(await AskAsync(client, Request4)); break;
                }
            }
        }

        private static async Task RunTwoStepRequestAsync(HttpClient client)
        {
            string generatedPrompt = await AskAsync(client, PromptGenerationRequest);
            Console.WriteLine("\nПромпт, составленный LLM:");
            Console.WriteLine(generatedPrompt);
            string solution = await AskAsync(client, generatedPrompt);
            Console.WriteLine("\nИтоговое решение:");
            Console.WriteLine(solution);
        }

        private static async Task<string> AskAsync(HttpClient client, string prompt)
        {
            var body = new Dictionary<string, object> { ["model"] = Model, ["input"] = prompt, ["store"] = false };
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            using (var content = new StringContent(serializer.Serialize(body), Encoding.UTF8, "application/json"))
            using (HttpResponseMessage response = await client.PostAsync(ApiUrl, content))
            {
                string json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException("OpenAI API вернул " + (int)response.StatusCode + " " + response.ReasonPhrase + ": " + ExtractApiError(json));
                return ExtractOutputText(json);
            }
        }

        private static string ExtractOutputText(string json)
        {
            var root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as Dictionary<string, object>;
            var result = new StringBuilder();
            if (root == null) throw new InvalidOperationException("OpenAI API вернул JSON неожиданного формата.");
            if (root.TryGetValue("output", out object outputObject))
            {
                foreach (object itemObject in EnumerateArray(outputObject))
                {
                    var item = itemObject as Dictionary<string, object>;
                    if (item == null || !item.TryGetValue("content", out object contentObject)) continue;
                    foreach (object partObject in EnumerateArray(contentObject))
                    {
                        var part = partObject as Dictionary<string, object>;
                        if (part != null && part.TryGetValue("type", out object type) &&
                            string.Equals(type as string, "output_text", StringComparison.Ordinal) &&
                            part.TryGetValue("text", out object text) && text is string partText)
                            result.Append(partText);
                    }
                }
            }
            if (result.Length == 0)
            {
                string status = root.TryGetValue("status", out object value) ? value as string : null;
                throw new InvalidOperationException("В ответе OpenAI API отсутствует текст результата (status: " + (status ?? "не указан") + ").");
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
                if (root != null && root.TryGetValue("error", out object errorObject) &&
                    errorObject is Dictionary<string, object> error && error.TryGetValue("message", out object message) &&
                    message is string messageText) return messageText;
            }
            catch { }
            return "подробности ответа не распознаны";
        }
    }
}
