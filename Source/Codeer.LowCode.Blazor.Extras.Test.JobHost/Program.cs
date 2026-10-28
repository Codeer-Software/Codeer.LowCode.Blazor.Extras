using Codeer.LowCode.Blazor.Extras.Server.Jobs;

//Jobs のテスト用の子プロセス。テンプレートの Program.cs と同じ形で JobMode を呼ぶ (DB は使わない)
if (args is not ["job", ..]) return 99;

return await JobMode.RunAsync(args[1..], ["Echo", "Fail", "Silent", "Partial", "Sleep"], async (name, jobArgs) =>
{
    switch (name)
    {
        case "Echo":
            //途中の出力 (最後の行だけが画面の文言になることの確認) と、受け取った内容の往復
            Console.WriteLine("progress 1");
            Console.WriteLine("progress 2");
            var userId = Environment.GetEnvironmentVariable(JobProcess.UserIdEnvironmentVariable);
            return $"{userId}|{jobArgs.Length}|{string.Join("/", jobArgs)}";
        case "Fail":
            Console.WriteLine("before failure");
            throw new InvalidOperationException("わざと失敗 (日本語)");
        case "Partial":
            //改行で終わらない出力を残す
            Console.Write("processing...");
            return "done";
        case "Sleep":
            //受け取ったミリ秒だけ動き続ける (実行中の判定の確認)
            await Task.Delay(int.Parse(jobArgs[0]));
            return "slept";
        default:
            return null;
    }
});
