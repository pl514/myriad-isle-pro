using System;
using System.Threading;
using System.Threading.Tasks;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 对话后端接口。
    //
    // 约定（下一阶段换 ApiBackend / WebBackend 时前端一行都不用改）：
    //   · onDelta 收到的是【本次新增的片段】，前端自己累加；
    //   · 正常结束或被取消时 Task 完成，不抛异常；
    //   · 出错直接抛异常，前端统一 catch 后显示错误气泡 + 重试按钮。
    // ---------------------------------------------------------------------
    internal interface IChatBackend
    {
        Task SendAsync(string text, Action<string> onDelta, CancellationToken token);
    }

    // 假后端：把一段写死的文本按 20~40ms 一批的节奏吐出来，验证流式渲染和动画。
    internal sealed class FakeBackend : IChatBackend
    {
        const string Answer = @"你好，我是 Capsule image Pro 里的 AI 面板。
现在是假后端，所以这段话是写死的，只用来验证三件事：
1. 面板能不能从胶囊形变展开；
2. 文字能不能一批一批地流出来；
3. 滚动条能不能自动跟到底。

下面是一段代码块，顺便验证等宽字体和换行：

```
internal static class Demo
{
    static void Main()
    {
        System.Console.WriteLine(42);
    }
}
```

接上真实模型后，这段文字会换成模型的实际回答。";

        public async Task SendAsync(string text, Action<string> onDelta, CancellationToken token)
        {
            if (onDelta == null) return;
            int start = 0;
            while (start < Answer.Length)
            {
                token.ThrowIfCancellationRequested();
                int take = Math.Min(2 + (start % 2), Answer.Length - start);
                onDelta(Answer.Substring(start, take));
                start += take;
                await Task.Delay(28, token);
            }
        }
    }
}
