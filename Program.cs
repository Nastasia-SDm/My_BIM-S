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
        private const int MaxOutputTokens = 700;

        // Этот снимок состояния намеренно передается без изменений во всех трех запросах.
        private const string ModelState = @"Виртуальные данные модели Revit

ДО запуска плагина:
- Стена W-101: железобетонная, 6000×3000×200 мм.
- Отверстие O-101 в стене W-101: 1200×900 мм.
- Армирование стены RA-W-101 связано со стеной W-101.
- Арматура лестничной площадки RA-LP-201: 3200×1800 мм; со стеной W-101, отверстием O-101 и армированием RA-W-101 не связана.
- Спецификация «Ведомость деталей» содержит позицию RA-LP-201 с габаритами 3200×1800 мм.
- Лист КЖ-12 содержит эту спецификацию и отображает 3200×1800 мм.

Действие:
- Плагин Revit работал со стеной W-101, отверстием O-101 и армированием стены RA-W-101.

ПОСЛЕ запуска плагина:
- Стена W-101, отверстие O-101 и армирование стены RA-W-101 остались без зафиксированных изменений.
- Арматура лестничной площадки RA-LP-201 стала 3180×1800 мм, то есть первый габарит уменьшился на 20 мм.
- В «Ведомости деталей» позиция RA-LP-201 обновилась до 3180×1800 мм.
- На листе КЖ-12 спецификация теперь отображает 3180×1800 мм.

Важно: это виртуальные данные; журнал транзакций, исходный код плагина и реальная Revit-модель отсутствуют. Отделяй факты от гипотез и не выдумывай недостающие доказательства.";

        private static readonly PromptCase[] Cases =
        {
            new PromptCase(
                "1. SIMPLE PROMPTING — temperature 0.1",
                0.1,
                "",
                "Сравни состояние модели ДО и ПОСЛЕ и кратко опиши, что произошло, опираясь только на приведенные данные."),
            new PromptCase(
                "2. SYSTEM PROMPT — temperature 0.4",
                0.4,
                "Ты BIM-координатор. Ты обнаружил неожиданное изменение после работы плагина Revit и пытаешься понять его возможную причину и последствия для модели и документации. Четко разделяй установленные факты, гипотезы и необходимые проверки.",
                "Проанализируй приведенное состояние модели ДО и ПОСЛЕ. Опиши изменение, возможные причины, последствия для модели и документации, а также необходимые проверки."),
            new PromptCase(
                "3. MULTI-PERSPECTIVE PROMPTING — temperature 0.6",
                0.6,
                "Исследуй ситуацию последовательно с двух профессиональных точек зрения. Сначала выступи BIM-координатором: исследуй модель физически, установи, почему могло возникнуть неожиданное изменение и к чему оно привело. Затем выступи разработчиком программы Revit: учитывая логику плагинов, предположи, какое действие могло затронуть несвязанную арматуру лестничной площадки. После этого сопоставь обе точки зрения и сформулируй общий вывод. Четко отличай факты от гипотез; отсутствующие сведения не выдумывай.",
                "Выполни пошаговый анализ приведенного состояния ДО и ПОСЛЕ: 1) взгляд BIM-координатора; 2) взгляд разработчика Revit; 3) сопоставление и общий вывод."),
        };

        private static void Main()
        {
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
            string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Переменная среды OPENAI_API_KEY не задана.");

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(2);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                foreach (PromptCase item in Cases)
                {
                    Console.WriteLine(new string('=', 72));
                    Console.WriteLine(item.Title);
                    Console.WriteLine(new string('=', 72));
                    Console.WriteLine(await AskAsync(client, item));
                    Console.WriteLine();
                }
            }
        }

        private static async Task<string> AskAsync(HttpClient client, PromptCase prompt)
        {
            var body = new Dictionary<string, object>
            {
                ["model"] = Model,
                ["input"] = ModelState + "\n\nЗадание:\n" + prompt.UserPrompt,
                ["temperature"] = prompt.Temperature,
                ["max_output_tokens"] = MaxOutputTokens,
                ["store"] = false
            };
            if (!string.IsNullOrEmpty(prompt.Instructions))
                body["instructions"] = prompt.Instructions;

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            string json = serializer.Serialize(body);
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            using (HttpResponseMessage response = await client.PostAsync(ApiUrl, content))
            {
                string responseJson = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(
                        "OpenAI API вернул " + (int)response.StatusCode + " " + response.ReasonPhrase + ": " + ExtractApiError(responseJson));

                return ExtractOutputText(responseJson);
            }
        }

        private static string ExtractOutputText(string json)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            var result = new StringBuilder();

            if (root == null)
                throw new InvalidOperationException("OpenAI API вернул JSON неожиданного формата.");

            // Некоторые представления Responses API содержат удобное агрегированное
            // поле output_text. В raw REST JSON текст обычно находится в
            // output[].content[].text, поэтому ниже поддерживаются обе формы.
            if (root.TryGetValue("output_text", out object aggregateText) && aggregateText is string textValue && !string.IsNullOrWhiteSpace(textValue))
                return textValue;

            if (root.TryGetValue("output", out object outputObject))
            {
                foreach (object outputItemObject in EnumerateJsonArray(outputObject))
                {
                    var outputItem = outputItemObject as Dictionary<string, object>;
                    if (outputItem == null || !outputItem.TryGetValue("content", out object contentObject))
                        continue;

                    foreach (object partObject in EnumerateJsonArray(contentObject))
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
                throw new InvalidOperationException(BuildMissingTextMessage(root));
            return result.ToString();
        }

        private static IEnumerable EnumerateJsonArray(object value)
        {
            // JavaScriptSerializer возвращает JSON-массивы как object[].
            // IEnumerable также оставляет парсер совместимым с ArrayList.
            if (value is IEnumerable items && !(value is string))
                return items;
            return Array.Empty<object>();
        }

        private static string BuildMissingTextMessage(Dictionary<string, object> root)
        {
            string status = root.TryGetValue("status", out object statusValue)
                ? statusValue as string
                : null;

            if (string.Equals(status, "incomplete", StringComparison.OrdinalIgnoreCase))
            {
                string reason = null;
                if (root.TryGetValue("incomplete_details", out object detailsObject) &&
                    detailsObject is Dictionary<string, object> details &&
                    details.TryGetValue("reason", out object reasonValue))
                {
                    reason = reasonValue as string;
                }

                return "OpenAI API вернул незавершенный ответ" +
                       (string.IsNullOrWhiteSpace(reason) ? "." : ": " + reason + ".");
            }

            if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                return "OpenAI API сообщил, что формирование ответа завершилось ошибкой.";

            return "В ответе OpenAI API отсутствует текст результата (status: " +
                   (string.IsNullOrWhiteSpace(status) ? "не указан" : status) + ").";
        }

        private static string ExtractApiError(string json)
        {
            try
            {
                var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                if (root != null && root.TryGetValue("error", out object errorObject) && errorObject is Dictionary<string, object> error && error.TryGetValue("message", out object message))
                    return (string)message;
            }
            catch { }
            return "подробности ответа не распознаны";
        }

        private sealed class PromptCase
        {
            public PromptCase(string title, double temperature, string instructions, string userPrompt)
            {
                Title = title;
                Temperature = temperature;
                Instructions = instructions;
                UserPrompt = userPrompt;
            }

            public string Title { get; }
            public double Temperature { get; }
            public string Instructions { get; }
            public string UserPrompt { get; }
        }
    }
}
