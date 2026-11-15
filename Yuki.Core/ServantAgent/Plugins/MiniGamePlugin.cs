using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;

namespace Yuki.Core.ServantAgent.Plugins;

public class MiniGamePlugin {
    private readonly Random _random = new();

    [KernelFunction]
    [Description("Подбрасываем кость/кубик что бы получить случайное число от 1 до 6")]
    public string RollTheDice() {
        try {
            var number = _random.Next(1, 7);
            return JsonSerializer.Serialize(new {
                status = "done",
                message = $"Выпало: {number}",
                result = number,
                min = 1,
                max = 6,
                game = "dice"
            });
        }
        catch (Exception) {
            return JsonSerializer.Serialize(new {
                status = "error",
                cause = "exception",
                description = "Кубик улетел слишком далеко"
            });
        }
    }

    [KernelFunction]
    [Description("Подбрасываем монетку что бы получить 'орёл' или 'решка'")]
    public string FlipCoin() {
        try {
            var number = _random.Next(1, 3);
            var result = number switch {
                1 => "Орёл",
                2 => "Решка",
                _ => "Ой, встала на ребро"
            };

            return JsonSerializer.Serialize(new {
                status = "done",
                message = result,
                resultThrow = result,
                game = "coin"
            });
        }
        catch (Exception) {
            return JsonSerializer.Serialize(new {
                status = "error",
                cause = "exception",
                description = "Ой, встала на ребро"
            });
        }
    }

    [KernelFunction]
    [Description("Случайное число от 1 до 10")]
    public string RandomNumberToTen() {
        var number = _random.Next(1, 11);
        return JsonSerializer.Serialize(new {
            status = "done",
            message = $"Случайное число: {number}",
            result = number,
            min = 1,
            max = 10,
            game = "random"
        });
    }

    [KernelFunction]
    [Description("Случайное число от 1 до 100")]
    public string RandomNumberToHundred() {
        var number = _random.Next(1, 101);
        return JsonSerializer.Serialize(new {
            status = "done",
            message = $"Случайное число: {number}",
            result = number,
            min = 1,
            max = 100,
            game = "random"
        });
    }

    [KernelFunction]
    [Description("Случайное число от 1 до число которое скажет пользователь")]
    public string RandomNumber([Description("Максимальное число")] int maxValue) {
        try {
            if (maxValue <= 1) {
                var fallback = _random.Next(1, 11);
                return JsonSerializer.Serialize(new {
                    status = "warning",
                    cause = "invalid_range",
                    description = "Укажите правильный диапазон (число должно быть больше 1)",
                    result = fallback,
                    min = 1,
                    max = 10,
                    game = "random"
                });
            }

            var number = _random.Next(1, maxValue + 1);
            return JsonSerializer.Serialize(new {
                status = "done",
                message = $"Случайное число: {number}",
                result = number,
                min = 1,
                max = maxValue,
                game = "random"
            });
        }
        catch (Exception ex) {
            var fallback = _random.Next(1, 11);
            return JsonSerializer.Serialize(new {
                status = "error",
                cause = "exception",
                description = $"{ex.Message}. Но я придумал {fallback}",
                result = fallback,
                min = 1,
                max = 10,
                game = "random"
            });
        }
    }
}