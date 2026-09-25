using AlphaQuantum.AIToolsBlocklist;var client=new AIToolsBlocklistClient(Environment.GetEnvironmentVariable("AQ_API_KEY")!);Console.WriteLine(await client.CheckAsync("chat.openai.com"));
