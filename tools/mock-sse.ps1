# tools/mock-sse.ps1 —— 模拟 OpenAI 兼容的流式接口，给 M2/M3 验收用（不参与发布）
#
# 用法（pwsh，不需要管理员权限）：
#   pwsh -NoProfile -File tools\mock-sse.ps1                 # 正常流式
#   pwsh -NoProfile -File tools\mock-sse.ps1 -Mode 401       # 验错误映射
#   pwsh -NoProfile -File tools\mock-sse.ps1 -Mode slow      # 验首字超时
#
# 然后把 build\data\models.txt 加一条：
#   mock|Mock 流式|http://127.0.0.1:11435/v1|mock||false
#
# 用 TcpListener 而不是 HttpListener：后者绑 127.0.0.1 需要 URL ACL（管理员权限）。

param(
    [int]$Port = 11435,
    [ValidateSet("ok", "401", "slow")]
    [string]$Mode = "ok"
)

$server = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
$server.Start()
Write-Host ("mock SSE on http://127.0.0.1:{0}/v1  (mode={1})  Ctrl+C 结束" -f $Port, $Mode)

while ($true) {
    $client = $server.AcceptTcpClient()
    $stream = $client.GetStream()
    $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::ASCII)

    # 读完请求头（到空行为止）；请求体我们不关心
    while ($true) {
        $line = $reader.ReadLine()
        if ([string]::IsNullOrEmpty($line)) { break }
    }

    if ($Mode -eq "401") {
        $msg = "HTTP/1.1 401 Unauthorized`r`nContent-Length: 0`r`nConnection: close`r`n`r`n"
        $b = [System.Text.Encoding]::ASCII.GetBytes($msg)
        $stream.Write($b, 0, $b.Length); $stream.Flush()
        $client.Close()
        continue
    }

    if ($Mode -eq "slow") { Start-Sleep -Seconds 40 }

    # 没有 Content-Length 也没有 chunked：HttpClient 会一直读到连接关闭，正好适合流式
    $head = "HTTP/1.1 200 OK`r`nContent-Type: text/event-stream`r`nConnection: close`r`n`r`n"
    $b = [System.Text.Encoding]::ASCII.GetBytes($head)
    $stream.Write($b, 0, $b.Length); $stream.Flush()

    foreach ($ch in "你好，这是 mock 后端的流式回答。".ToCharArray()) {
        $json = 'data: {"choices":[{"delta":{"content":"' + $ch + '"}}]}' + "`n`n"
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
        $stream.Write($bytes, 0, $bytes.Length); $stream.Flush()
        Start-Sleep -Milliseconds 40
    }

    # 故意发一个 content:null 的收尾块，用来验证"不能把 finish_reason 当正文"的修复
    $tail = 'data: {"choices":[{"delta":{"content":null},"finish_reason":"stop"}]}' + "`n`n"
    $tb = [System.Text.Encoding]::UTF8.GetBytes($tail)
    $stream.Write($tb, 0, $tb.Length)

    $done = [System.Text.Encoding]::UTF8.GetBytes("data: [DONE]`n`n")
    $stream.Write($done, 0, $done.Length); $stream.Flush()

    $client.Close()
}
