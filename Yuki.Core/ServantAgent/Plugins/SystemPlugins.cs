using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace Yuki.Core.ServantAgent.Plugins;

public class SystemPlugins {
    [KernelFunction("get_current_time")]
    [Description("Возвращает текущее системное время и дату.")]
    public string GetCurrentTime() => DateTime.Now.ToString();
}
