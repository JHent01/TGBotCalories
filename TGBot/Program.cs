using System.Text.Json;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

var keysPath = Path.Combine(AppContext.BaseDirectory,  "keys.json");
string? keyTG = "";
string? keyAI = "";
bool keysLoaded = true;
if (!File.Exists(keysPath))
{
    try
    {
        keyTG = Environment.GetEnvironmentVariable("TelegramBotToken");
        keyAI = Environment.GetEnvironmentVariable("GroqApiKey");
        keysLoaded = false;
    }
    catch 
    {
        throw new FileNotFoundException($"Файл с ключами не найден: {keysPath}. Создайте keys.json на основе keys.example.json.");
    }
}
string token;
string groqApiKey;
if (keysLoaded)
{
    using var keysFile = File.OpenRead(keysPath);
    var keys = JsonSerializer.Deserialize<JsonElement>(keysFile);

      token = keys.GetProperty("TelegramBotToken").GetString()
        ?? throw new InvalidOperationException("TelegramBotToken не задан в keys.json.");
     groqApiKey = keys.GetProperty("GroqApiKey").GetString()
      ?? throw new InvalidOperationException("GroqApiKey не задан в keys.json.");
     
}
else
{
    token = keyTG ?? throw new InvalidOperationException("TelegramBotToken не задан в переменных окружения.");
    groqApiKey = keyAI ?? throw new InvalidOperationException("GroqApiKey не задан в переменных окружения.");
}
var bot = new TelegramBotClient(token); 

var me = await bot.GetMe();
Console.WriteLine($"Бот запущен: @{me.Username}");

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

bot.StartReceiving(
    updateHandler: HandleUpdateAsync,
    errorHandler: HandleErrorAsync,
    receiverOptions: new ReceiverOptions
    {
        AllowedUpdates = [UpdateType.Message]
    },
    cancellationToken: cts.Token
);

Console.WriteLine("Нажмите Ctrl+C для остановки.");
await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => { });

async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
{
    bool isCommented = false;
    if (update.Message is not { } message)
        return;

    if (message.Photo is not { } photos || photos.Length == 0)
    {
        //await botClient.SendMessage(
        //    chatId: message.Chat.Id,
        //    text: "Пришли мне фотографию с едой, и я подсчитаю калории! 🍽️",
        //    cancellationToken: cancellationToken
        //);
        return;
    }

    await botClient.SendMessage(
        chatId: message.Chat.Id,
        text: "⏳ Анализирую фото, подсчитываю калории...",
        cancellationToken: cancellationToken
    );

    try
    {
        string comment = "";
        var bestPhoto = photos[^1];
        if (message.Caption != null)
        {
            comment = message.Caption ?? "";
            isCommented = true;
            
        }
        var file = await botClient.GetFile(bestPhoto.FileId, cancellationToken);
        var fileUrl = $"https://api.telegram.org/file/bot{token}/{file.FilePath}";

        using var httpClient = new HttpClient();
        var imageBytes = await httpClient.GetByteArrayAsync(fileUrl, cancellationToken);
        var base64Image = Convert.ToBase64String(imageBytes);

        var prompt = "Ты — ИИ-диетолог и анализатор еды." +

            "Твоя задача — анализировать фото еды, которое пользователь отправляет, определять все блюда и ингредиенты на изображении и максимально точно оценивать их калорийность и БЖУ." +
            "Главное правило:" +
            "Не придумывай данные и не искажай оценки. Не преуменьшай и не преувеличивай калорийность. Используй максимально реалистичную и объективную оценку на основе:" +
            "размера порции" +
            "видимых ингредиентов" +
            "способа приготовления" +
            "средней калорийности продуктов"+
            "типичных рецептов" + 
            "Если точность определить невозможно —  указывай наиболее вероятную оценку с учетом визуального анализа." +
            (isCommented ? $"Вот дополнительная информация: {comment}" : "") +
            "Правила:" +
            "Определи каждое отдельное блюдо или продукт на фото." +
            "Для каждого блюда укажи:" +
            "название"+
            "примерный вес порции в граммах"+
            "калории"+
            "БЖУ (белки, жиры, углеводы)\r\nОтвет всегда оформляй строго в таком формате:"+
            "Блюдо 1 — XXX г — XXX ккал (Б: XX г / Ж: XX г / У: XX г)"+
            "Блюдо 2 — XXX г — XXX ккал (Б: XX г / Ж: XX г / У: XX г)"+
            "Блюдо 3 — XXX г — XXX ккал (Б: XX г / Ж: XX г / У: XX г)"+
            "Общее количество калорий: XXX ккал"+
            "Общее БЖУ:"+
            "Белки: XX г"+
            "Жиры: XX г"+
            "Углеводы: XX г" +

            "Если блюдо невозможно определить точно — укажи наиболее вероятный вариант." +
            "Если на фото несколько продуктов смешаны — оцени состав максимально объективно." +
            "Не добавляй лишнего текста, объяснений, предупреждений или дисклеймеров." +
            "Пиши ответ только на русском языке." +
            "Если виден бренд, упаковка или размер порции — учитывай это при расчете." +
            "При сомнениях выбирай среднее реалистичное значение, а не минимальное или максимальное." +
            "Старайся анализировать:" +
            "количество масла" +
            "панировку" +
            "соусы" +
            "сахар" +
            "жарку/запекание"+
            "напитки"+
            "скрытые калории"+
            "Твоя цель — дать максимально честную и точную оценку калорий и БЖУ по фото.";

        var groqRequest = new
        {
            model = "meta-llama/llama-4-scout-17b-16e-instruct",
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = prompt },
                        new
                        {
                            type = "image_url",
                            image_url = new
                            {
                                url = $"data:image/jpeg;base64,{base64Image}"
                            }
                        }
                    }
                }
            },
            temperature = 0.7,
            max_tokens = 1024
        };

        var groqHttpClient = new HttpClient();
        groqHttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {groqApiKey}");

        var jsonContent = JsonSerializer.Serialize(groqRequest);
        var requestContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var groqResponse = await groqHttpClient.PostAsync(
            "https://api.groq.com/openai/v1/chat/completions",
            requestContent,
            cancellationToken
        );

        var responseText = await groqResponse.Content.ReadAsStringAsync(cancellationToken);
         
        if (!groqResponse.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"Groq API ошибка: {groqResponse.StatusCode} - {responseText}");

            string userErrorMessage = "❌ Ошибка API. Попробуй ещё раз позже.";
            try
            {
                var errorJson = JsonSerializer.Deserialize<JsonElement>(responseText);
                if (errorJson.TryGetProperty("error", out var errorObj) &&
                    errorObj.TryGetProperty("code", out var code) &&
                    code.GetString() == "rate_limit_exceeded" &&
                    errorObj.TryGetProperty("message", out var errorMsg))
                {
                    var msgText = errorMsg.GetString() ?? "";
                    var match = System.Text.RegularExpressions.Regex.Match(msgText, @"try again in ([\d.]+)s");
                    if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double seconds))
                    {
                        int roundedSeconds = (int)Math.Ceiling(seconds);
                        userErrorMessage = $"⏳ Слишком много запросов, попробуйте через {roundedSeconds} секунд.";
                    }
                    else
                    {
                        userErrorMessage = "⏳ Слишком много запросов, попробуйте позже.";
                    }
                }
            }
            catch { }

            await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: userErrorMessage,
                cancellationToken: cancellationToken
            );
            return;
        }

        var responseJson = JsonSerializer.Deserialize<JsonElement>(responseText);
         
        string result;
        if (responseJson.TryGetProperty("choices", out var choices) && 
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var responseMessage) &&
            responseMessage.TryGetProperty("content", out var content))
        {
            result = content.GetString() ?? "Не удалось проанализировать фото.";
        }
        else
        {
            Console.Error.WriteLine($"Неожиданная структура ответа: {responseText}");
            result = "Не удалось проанализировать фото.";
        }

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: $"🔥 Подсчёт калорий:\n\n{result}",
            cancellationToken: cancellationToken
        );
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Ошибка анализа фото: {ex.Message}");
        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: "❌ Не удалось проанализировать фото. Попробуй ещё раз.",
            cancellationToken: cancellationToken
        );
    }
}

Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
{
    Console.Error.WriteLine($"Ошибка [{source}]: {exception.Message}");
    return Task.CompletedTask;
}

