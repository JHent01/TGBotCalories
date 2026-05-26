using System.Text.Json;
using Mscc.GenerativeAI;
using Mscc.GenerativeAI.Types;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

var keysPath = Path.Combine(AppContext.BaseDirectory, "..", "keys.json");
if (!File.Exists(keysPath))
    throw new FileNotFoundException($"Файл с ключами не найден: {keysPath}. Создайте keys.json на основе keys.example.json.");

using var keysFile = File.OpenRead(keysPath);
var keys = JsonSerializer.Deserialize<JsonElement>(keysFile);

var token = keys.GetProperty("TelegramBotToken").GetString()
    ?? throw new InvalidOperationException("TelegramBotToken не задан в keys.json.");
var geminiApiKey = keys.GetProperty("GeminiApiKey").GetString()
    ?? throw new InvalidOperationException("GeminiApiKey не задан в keys.json.");

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
        var bestPhoto = photos[^1];
        var file = await botClient.GetFile(bestPhoto.FileId, cancellationToken);
        var fileUrl = $"https://api.telegram.org/file/bot{token}/{file.FilePath}";
         
        var googleAI = new GoogleAI(apiKey: geminiApiKey);
        var model = googleAI.GenerativeModel(model: Model.Gemini25Flash);

        var prompt = "На этом фото есть еда. Определи все блюда и продукты, которые видишь. " +
                     "Для каждого укажи примерное количество калорий. " +
                     "В конце укажи общее количество калорий. " +
                     "И Рапиши примерное БЖУ (белки, жиры, углеводы) для каждого блюда. " +
                     "Затем напиши общие БЖУ для всего."+
                     "Отвечай на русском языке. Будь краток и конкретен."+
                     "Дай Ответ в формате списка."+
                    " так что б это выглядело примерно так:"+
                    "Список блюд с калориями и БЖУ"+
                    "Общее количество калорий и БЖУ"
                    
        ;

        var request = new GenerateContentRequest(prompt);
        await request.AddMedia(fileUrl);

        var response = await model.GenerateContent(request);
        var result = response.Text ?? "Не удалось проанализировать фото.";

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

