using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;

namespace Yuki.Core.ServantAgent.Plugins;

public class SystemPlugins {
    [KernelFunction("get_current_time")]
    [Description("Возвращает текущее системное время и дату.")]
    public string GetCurrentTime() {
        var now = DateTime.Now;
        return JsonSerializer.Serialize(new {
            status = "DONE",
            message = now.ToString("HH:mm"),
            data = new {
                fullDate = now.ToString("dd.MM.yyyy"),
                time = now.ToString("HH:mm")
            }
        });
    }
}
