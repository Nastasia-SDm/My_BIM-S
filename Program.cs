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
        private const int ConstrainedMaxOutputTokens = 300;

        // Этот снимок состояния намеренно передается без изменений в обоих режимах.
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

        private const string OriginalPrompt =
            "Сравни состояние модели ДО и ПОСЛЕ и кратко опиши, что произошло, опираясь только на приведенные данные.";

        private const string ConstrainedInstructions =
            "Ответь строго одним JSON-объектом ровно с четырьмя строковыми полями: " +
            "change — что изменилось в модели; consequences — к каким изменениям в спецификации и листе это привело; " +
            "checks — что необходимо проверить в Revit; conclusion — вывод о характере изменения, неожиданное оно или ожидаемое. " +
            "Значение каждого поля должно содержать не более одного-двух коротких предложений. " +
            "Значение поля conclusion должно заканчиваться точной строкой END_BIM_S. После JSON-объекта не выводи ничего.";

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
            Console.WriteLine("1 — Без ограничений");
            Console.WriteLine("2 — С ограничениями");
            Console.Write("Выберите режим: ");
            string choice = Console.ReadLine();

            bool constrained;
            if (choice == "1")
                constrained = false;
            else if (choice == "2")
                constrained = true;
            else
                throw new InvalidOperationException("Введите 1 или 2.");

            string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Переменная среды OPENAI_API_KEY не задана.");

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(2);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                Console.WriteLine();
                Console.WriteLine(new string('=', 72));
                Console.WriteLine(constrained ? "2 — С ограничениями" : "1 — Без ограничений");
                Console.WriteLine(new string('=', 72));
                if (constrained)
                {
                    Console.WriteLine("Примечание: Responses API не поддерживает параметр stop; используется явная инструкция завершения END_BIM_S.");
                    Console.WriteLine();
                }

                string answer = await AskAsync(client, constrained);
                Console.WriteLine(constrained ? FormatJsonForConsole(answer) : answer);
            }
        }

        private static async Task<string> AskAsync(HttpClient client, bool constrained)
        {
            var body = new Dictionary<string, object>
            {
                ["model"] = Model,
                ["input"] = ModelState + "\n\nЗадание:\n" + OriginalPrompt,
                ["store"] = false
            };

            if (constrained)
            {
                body["instructions"] = ConstrainedInstructions;
                body["max_output_tokens"] = ConstrainedMaxOutputTokens;
                body["text"] = CreateJsonOutputFormat();
            }

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

        private static Dictionary<string, object> CreateJsonOutputFormat()
        {
            var properties = new Dictionary<string, object>
            {
                ["change"] = CreateShortStringProperty("Что изменилось в модели."),
                ["consequences"] = CreateShortStringProperty("К каким изменениям в спецификации и листе это привело."),
                ["checks"] = CreateShortStringProperty("Что необходимо проверить в Revit."),
                ["conclusion"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Одно-два коротких предложения о том, ожидаемое это изменение или неожиданное. Значение должно заканчиваться точной строкой END_BIM_S."
                }
            };

            return new Dictionary<string, object>
            {
                ["format"] = new Dictionary<string, object>
                {
                    ["type"] = "json_schema",
                    ["name"] = "bim_analysis",
                    ["strict"] = true,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = properties,
                        ["required"] = new[] { "change", "consequences", "checks", "conclusion" },
                        ["additionalProperties"] = false
                    }
                }
            };
        }

        private static Dictionary<string, object> CreateShortStringProperty(string meaning)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = meaning + " Не более одного-двух коротких предложений."
            };
        }

        private static string FormatJsonForConsole(string json)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var data = serializer.DeserializeObject(json) as Dictionary<string, object>;
            string[] fields = { "change", "consequences", "checks", "conclusion" };

            if (data == null || data.Count != fields.Length)
                throw new InvalidOperationException("Ответ ограниченного режима не является JSON-объектом с четырьмя полями.");

            var result = new StringBuilder();
            result.AppendLine("{");
            for (int index = 0; index < fields.Length; index++)
            {
                string field = fields[index];
                if (!data.TryGetValue(field, out object value) || !(value is string))
                    throw new InvalidOperationException("В JSON-ответе отсутствует строковое поле " + field + ".");

                result.Append("  ");
                result.Append(serializer.Serialize(field));
                result.Append(": ");
                result.Append(serializer.Serialize(value));
                result.AppendLine(index < fields.Length - 1 ? "," : "");
            }
            result.Append("}");
            return result.ToString();
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

    }
}
