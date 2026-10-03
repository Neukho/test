using System;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.Tycoon;
using MorpheHouse.Save;
using System.IO;
using System.Xml.Serialization;

// Unity 없이 순수 로직(Core/Data/Tycoon)을 검증하는 콘솔 하네스.
// 빌드/실행: tools/csharp_check/run.sh
public static class Program
{
    public static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : "balance";
        FileTextSource src = new FileTextSource(dir);
        int failures = 0;

        // 1) CSV 파서
        CsvTable t = CsvTable.Parse("﻿a,b\r\n1,\"x,y\"\r\n\r\n2,z\r\n");
        failures += Check("csv rows", t.Rows.Count == 2);
        failures += Check("csv quoted", t.Rows[0].Str("b") == "x,y");

        // 2) EventBus
        int hits = 0;
        Action<GoldChanged> h = e => hits++;
        EventBus.Subscribe(h);
        EventBus.Publish(new GoldChanged(1, 1));
        EventBus.Unsubscribe(h);
        EventBus.Publish(new GoldChanged(1, 1));
        failures += Check("eventbus", hits == 1);

        // 3) 난이도별 자동 플레이
        foreach (Difficulty d in new[] { Difficulty.Story, Difficulty.Normal, Difficulty.Hard })
        {
            BalanceTable b = BalanceTable.Load(src, d);
            AutoPlayer.Summary s = AutoPlayer.Run(b, 200, 1234);
            Console.WriteLine("[{0}] days={1} gold={2:F0} rep={3:F0} level={4:F1}", d, s.Days, s.FinalGold, s.FinalReputation, s.FinalLevel);
            Console.Write("   avg gross/day by chapter:");
            for (int i = 0; i < s.ChapterAverageGross.Length; i++) Console.Write(" {0:F0}", s.ChapterAverageGross[i]);
            Console.WriteLine();
            failures += Check(d + " days==64", s.Days == 64);
            failures += Check(d + " gold>0", s.FinalGold > 0);
        }

        // 4) 상태 머신 순서 강제
        BalanceTable nb = BalanceTable.Load(src, Difficulty.Normal);
        GameState st = GameState.NewGame(nb, Difficulty.Normal, "t");
        DayCycle c = new DayCycle(st, nb, new SystemRandomAdapter(1));
        c.Begin();
        bool threw = false;
        try { c.Settle(); } catch (InvalidOperationException) { threw = true; }
        failures += Check("phase order enforced", threw);

        failures += SaveTests(nb);

        Console.WriteLine(failures == 0 ? "ALL OK" : failures + " FAILURE(S)");
        return failures == 0 ? 0 : 1;
    }

    // Unity 의 JsonUtility 대신 XmlSerializer 로 SaveService 의 서명·백업·검증 로직을 검증한다.
    private sealed class XmlSer : ISerializer
    {
        private readonly XmlSerializer _x = new XmlSerializer(typeof(SaveData));
        public string ToJson(SaveData d) { using (StringWriter w = new StringWriter()) { _x.Serialize(w, d); return w.ToString(); } }
        public SaveData FromJson(string s) { using (StringReader r = new StringReader(s)) { return (SaveData)_x.Deserialize(r); } }
    }

    private static int SaveTests(BalanceTable nb)
    {
        int f = 0;
        string dir = Path.Combine(Path.GetTempPath(), "morphe_save_test_" + Guid.NewGuid().ToString("N"));
        SaveService svc = new SaveService(new XmlSer(), new FileSaveStorage(dir));

        GameState s = GameState.NewGame(nb, Difficulty.Hard, "라이언");
        s.Gold = 1234; s.Chapter = 3; s.Day = 20; s.DayInChapter = 4; s.Chars["howl"].Aff = 42;
        svc.Save(1, SaveMapper.ToSave(s, 1, "0.1.0"));
        LoadResult ok = svc.Load(1);
        f += Check("save roundtrip", ok.Status == LoadStatus.Ok && SaveMapper.FromSave(ok.Data).Chars["howl"].Aff == 42 && ok.Data.Gold == 1234);

        // 변조: 골드 값을 편집하면 서명 불일치
        string path = Path.Combine(dir, "slot1.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("<Gold>1234</Gold>", "<Gold>999999</Gold>"));
        LoadResult t = svc.Load(1);
        f += Check("tamper detected (no backup)", t.Status == LoadStatus.Tampered);

        // 두 번째 저장 후 본 파일 손상 → 백업 복구
        svc.Save(2, SaveMapper.ToSave(s, 2, "0.1.0"));
        s.Gold = 2000; svc.Save(2, SaveMapper.ToSave(s, 2, "0.1.0"));
        File.WriteAllText(Path.Combine(dir, "slot2.json"), "garbage");
        LoadResult b = svc.Load(2);
        f += Check("restored from backup", b.Status == LoadStatus.RestoredFromBackup && b.Data.Gold == 1234);

        // 범위 검증
        s.Gold = 5; s.Reputation = 500;
        bool refused = false;
        try { svc.Save(3, SaveMapper.ToSave(s, 3, "0.1.0")); } catch (InvalidOperationException) { refused = true; }
        f += Check("invalid data refused", refused);

        // 아침이 아닌 단계 저장 거부
        s.Reputation = 50; s.Phase = DayPhase.Night;
        bool phaseRefused = false;
        try { SaveMapper.ToSave(s, 1, "0.1.0"); } catch (InvalidOperationException) { phaseRefused = true; }
        f += Check("non-morning save refused", phaseRefused);

        f += Check("missing slot", svc.Load(3).Status == LoadStatus.NotFound);
        Directory.Delete(dir, true);
        return f;
    }

    private static int Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        return ok ? 0 : 1;
    }
}
