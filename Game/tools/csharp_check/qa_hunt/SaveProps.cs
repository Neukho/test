using System;
using System.Collections.Generic;
using System.Text;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.GameFlow;
using MorpheHouse.Save;

namespace QaHunt
{
    public static class SaveProps
    {
        private static float RF(Random r, float lo, float hi) { return (float)(lo + r.NextDouble() * (hi - lo)); }

        public static GameState RandomState(World w, BalanceTable b, Random r)
        {
            GameState s = GameState.NewGame(b, (Difficulty)r.Next(3), "이름" + r.Next(1000));
            string[] genders = { "male", "female", "nonbinary", "neutral" };
            s.Gender = genders[r.Next(4)];
            string[] sp = { "human", "wolf", "fox", "tiger", "rabbit", "bear", "cat" };
            s.PlayerSpecies = sp[r.Next(sp.Length)];
            s.PlayerAppearance = 1 + r.Next(6);
            s.Chapter = 1 + r.Next(8);
            s.DayInChapter = 1 + r.Next(b.Chapter(s.Chapter).Days);
            s.Day = Math.Max(s.DayInChapter, 1 + r.Next(64));
            s.Phase = DayPhase.Morning;
            s.Gold = RF(r, -5000f, 200000f);
            s.Reputation = RF(r, 0f, 100f);
            s.Fatigue = RF(r, 0f, 100f);
            s.ShopLevel = 1 + r.Next(10);
            s.SatisfactionBonus = r.Next(2) == 0 ? 0f : 0.03f;
            s.PlayTimeSec = r.NextDouble() * 100000.0;
            foreach (string id in CharIds.All)
            {
                CharState c = s.Chars[id];
                c.Aff = RF(r, 0f, 100f); c.Trust = RF(r, 0f, 50f); c.EventsDone = r.Next(6);
                c.LastGiftDay = r.Next(3) == 0 ? -99 : r.Next(64); c.LastDateDay = r.Next(3) == 0 ? -99 : r.Next(64); c.LastContactDay = r.Next(64);
            }
            int nf = r.Next(40);
            for (int i = 0; i < nf; i++) s.Flags["flag_" + r.Next(500)] = r.Next(4) == 0 ? 0 : (r.Next(3) == 0 ? r.Next(-5, 100) : 1);
            int lock_ = r.Next(5); // 0 none,1 single,2 duo,3 trio, 4 legacy single (LockedRoute only)
            List<string> pool = new List<string>(CharIds.All);
            List<string> members = new List<string>();
            int k = lock_ == 4 ? 1 : lock_;
            for (int i = 0; i < k; i++) { int j = r.Next(pool.Count); members.Add(pool[j]); pool.RemoveAt(j); }
            if (lock_ == 4) s.LockedRoute = members[0]; else if (k > 0) s.SetLock(members);
            int nv = r.Next(60);
            for (int i = 0; i < nv; i++) s.VisitedNodes.Add("Node_" + r.Next(2000) + "_" + i);
            return s;
        }

        public static string Diff(GameState a, GameState b)
        {
            if (a.Difficulty != b.Difficulty) return "Difficulty";
            if (a.PlayerName != b.PlayerName) return "PlayerName";
            if (a.Gender != b.Gender) return "Gender";
            if (a.PlayerSpecies != b.PlayerSpecies) return "Species";
            if (a.PlayerAppearance != b.PlayerAppearance) return "Appearance";
            if (a.Chapter != b.Chapter || a.Day != b.Day || a.DayInChapter != b.DayInChapter) return "chapter/day";
            if (a.Phase != b.Phase) return "Phase";
            if (a.Gold != b.Gold || a.Reputation != b.Reputation || a.Fatigue != b.Fatigue) return "gold/rep/fatigue";
            if (a.ShopLevel != b.ShopLevel) return "ShopLevel";
            if (a.SatisfactionBonus != b.SatisfactionBonus) return "SatisfactionBonus";
            if (a.LockedRoute != b.LockedRoute) return "LockedRoute '" + a.LockedRoute + "' vs '" + b.LockedRoute + "'";
            if (a.LockedGroup.Count != b.LockedGroup.Count) return "LockedGroup count";
            for (int i = 0; i < a.LockedGroup.Count; i++) if (a.LockedGroup[i] != b.LockedGroup[i]) return "LockedGroup order";
            if (!a.LockedIds().SequenceEqualStr(b.LockedIds())) return "LockedIds()";
            if (a.PlayTimeSec != b.PlayTimeSec) return "PlayTimeSec " + a.PlayTimeSec + " vs " + b.PlayTimeSec;
            foreach (KeyValuePair<string, CharState> p in a.Chars)
            {
                CharState o;
                if (!b.Chars.TryGetValue(p.Key, out o)) return "char missing " + p.Key;
                CharState c = p.Value;
                if (c.Aff != o.Aff || c.Trust != o.Trust || c.EventsDone != o.EventsDone || c.LastGiftDay != o.LastGiftDay || c.LastDateDay != o.LastDateDay || c.LastContactDay != o.LastContactDay) return "char " + p.Key;
            }
            if (a.Flags.Count != b.Flags.Count) return "flag count";
            foreach (KeyValuePair<string, int> p in a.Flags) { int v; if (!b.Flags.TryGetValue(p.Key, out v) || v != p.Value) return "flag " + p.Key; }
            if (a.VisitedNodes.Count != b.VisitedNodes.Count) return "visited count";
            for (int i = 0; i < a.VisitedNodes.Count; i++) if (a.VisitedNodes[i] != b.VisitedNodes[i]) return "visited order";
            return null;
        }

        private static LoadStatus FutureVersion(XmlSer ser, GameState s)
        {
            MemStorage ms = new MemStorage(); SaveService sv = new SaveService(ser, ms);
            sv.Save(1, SaveMapper.ToSave(s, 1, "qa"));
            ms.Files["slot1.json"] = ms.Files["slot1.json"].Replace("<Version>" + SaveData.CurrentVersion + "</Version>", "<Version>" + (SaveData.CurrentVersion + 1) + "</Version>");
            return sv.Load(1).Status;
        }

        public static void Run(World w)
        {
            Report.Header("SAVE / LOAD PROPERTY TESTS");
            BalanceTable b = w.Balance(Difficulty.Normal);
            Random r = new Random(20261003);
            SaveService svc = new SaveService(new XmlSer(), new MemStorage());
            int fails = 0, n = 2000;
            string first = null;
            for (int i = 0; i < n; i++)
            {
                GameState s = RandomState(w, b, r);
                SaveData d = SaveMapper.ToSave(s, 1 + i % 3, "qa");
                svc.Save(1 + i % 3, d);
                LoadResult lr = svc.Load(1 + i % 3);
                if (lr.Status != LoadStatus.Ok) { fails++; if (first == null) first = "status " + lr.Status + " " + lr.Detail; continue; }
                GameState t = SaveMapper.FromSave(lr.Data);
                string diff = Diff(s, t);
                if (diff != null) { fails++; if (first == null) first = diff; }
            }
            Report.Check("round-trip equality of " + n + " random valid states (all fields incl. LockedGroup/VisitedNodes/flags/legacy single lock)", fails == 0, fails + " fails, first=" + first);

            // second round trip is idempotent (load -> save -> load)
            int idem = 0;
            for (int i = 0; i < 300; i++)
            {
                GameState s = RandomState(w, b, r);
                svc.Save(1, SaveMapper.ToSave(s, 1, "qa"));
                GameState t = SaveMapper.FromSave(svc.Load(1).Data);
                svc.Save(2, SaveMapper.ToSave(t, 2, "qa"));
                GameState u = SaveMapper.FromSave(svc.Load(2).Data);
                if (Diff(t, u) != null) idem++;
            }
            Report.Check("save(load(save(x))) is idempotent", idem == 0, idem + " diffs");

            // save only in Morning
            int notThrown = 0;
            foreach (DayPhase ph in new DayPhase[] { DayPhase.Business, DayPhase.Night, DayPhase.Settlement })
            {
                GameState s = RandomState(w, b, r); s.Phase = ph;
                try { SaveMapper.ToSave(s, 1, "qa"); notThrown++; } catch (InvalidOperationException) { }
            }
            Report.Check("ToSave refuses Business/Night/Settlement", notThrown == 0);

            // ---- QA-06: every historical v1 schema variant (old text-hash signing) still loads Ok, and is migrated to v2 ----
            {
                XmlSer ser = new XmlSer();
                // variant (a) stage 3 (full current schema, empty or non-empty LockedGroup), (b) stage 2 (no LockedGroup), (c) stage 1 (no PlayerSpecies/PlayerAppearance/VisitedNodes/LockedGroup)
                int bad = 0; string firstBad = null; int tried = 0;
                for (int i = 0; i < 60; i++)
                {
                    GameState s = RandomState(w, b, r);
                    int variant = i % 3;
                    if (variant >= 1) { s.SetLock(new string[0]); s.LockedGroup.Clear(); }
                    if (variant == 1 && r.Next(2) == 0) s.LockedRoute = CharIds.All[r.Next(CharIds.All.Length)];
                    if (variant == 2) { s.LockedRoute = ""; s.PlayerSpecies = "human"; s.PlayerAppearance = 1; s.VisitedNodes.Clear(); }
                    SaveData d = SaveMapper.ToSave(s, 1, "qa");
                    d.Version = 1; d.Hash = string.Empty;
                    string basis = ser.ToJson(d);
                    if (variant >= 1) basis = SaveService.StripEmptyLockedGroup(basis);
                    if (variant == 2)
                    {
                        basis = SaveService.StripLegacyField(basis, "PlayerSpecies"); basis = SaveService.StripLegacyField(basis, "PlayerAppearance"); basis = SaveService.StripLegacyField(basis, "VisitedNodes");
                        // independent of the production stripper: the stage-1 text really lacks the elements
                        if (basis.Contains("PlayerSpecies") || basis.Contains("VisitedNodes")) { bad++; if (firstBad == null) firstBad = "strip"; continue; }
                    }
                    string hash = SaveIntegrity.Compute(basis);
                    string file = basis.Replace("<Hash />", "<Hash>" + hash + "</Hash>").Replace("<Hash></Hash>", "<Hash>" + hash + "</Hash>");
                    // what the real old writer produced: the stripped text IS the file (the old field elements are simply absent)
                    MemStorage ms = new MemStorage(); ms.Files["slot1.json"] = file;
                    LoadResult lr = new SaveService(ser, ms).Load(1);
                    tried++;
                    string diffs = lr.Status == LoadStatus.Ok ? SaveProps.Diff(s, SaveMapper.FromSave(lr.Data)) : "status " + lr.Status + " " + lr.Detail;
                    if (diffs != null || lr.Data.Version != SaveData.CurrentVersion || lr.FileVersion != 1) { bad++; if (firstBad == null) firstBad = "variant " + variant + ": " + (diffs ?? "version " + lr.Data.Version + "/" + lr.FileVersion); }
                    // tampering with any v1 variant is still detected
                    ms.Files["slot1.json"] = file.Replace("<Gold>" + d.Gold.ToString(System.Globalization.CultureInfo.InvariantCulture) + "</Gold>", "<Gold>123456</Gold>");
                    if (ms.Files["slot1.json"] != file && new SaveService(ser, ms).Load(1).Status != LoadStatus.Tampered) { bad++; if (firstBad == null) firstBad = "v1 tamper variant " + variant; }
                }
                Report.Check("[QA-06] 60 legacy v1 saves (stage-1 / stage-2 / stage-3 schemas, old text-hash signing) load Ok, equal state, migrate to v" + SaveData.CurrentVersion + "; edits to them are Tampered", bad == 0 && tried == 60, bad + " bad, first=" + firstBad + ", tried=" + tried);
            }

            // ---- QA-06: v2 canonical payload: forward compatibility + tamper coverage ----
            {
                XmlSer ser = new XmlSer(); MemStorage ms = new MemStorage(); SaveService sv = new SaveService(ser, ms);
                GameState s = RandomState(w, b, r); s.Gold = 4321; s.SetLock(new string[] { "howl", "johan" }); s.VisitedNodes.Add("N_a"); s.Flags["f_x"] = 3;
                sv.Save(1, SaveMapper.ToSave(s, 1, "qa"));
                string file = ms.Files["slot1.json"];
                Report.Check("[QA-06] new saves are written as v" + SaveData.CurrentVersion, SaveData.CurrentVersion >= 2 && file.Contains("<Version>" + SaveData.CurrentVersion + "</Version>"));
                // unknown (future) element and removed optional/default element do not invalidate the signature
                string future = file.Replace("<Hash>", "<FutureField>zzz</FutureField><Hash>");
                if (future == file) future = file.Replace("</SaveData>", "<FutureField>zzz</FutureField></SaveData>");
                ms.Files["slot1.json"] = future;
                LoadResult lr = sv.Load(1);
                Report.Check("[QA-06] a file carrying an extra unknown field still verifies (serializer-independent signing)", lr.Status == LoadStatus.Ok, lr.Status + " " + lr.Detail);
                // serializer independence: signature is over fields, not over text layout
                SaveData loaded = ser.FromJson(file);
                string pay1 = SaveIntegrity.CanonicalPayload(loaded);
                loaded.Hash = "ignored";
                Report.Check("[QA-06] canonical payload ignores Hash and equals across re-serialisation", pay1 == SaveIntegrity.CanonicalPayload(ser.FromJson(ser.ToJson(loaded))) && SaveIntegrity.Sign(loaded) == SaveIntegrity.Sign(ser.FromJson(file)));
                // every SaveData field is either signed or explicitly excluded
                List<string> missing = new List<string>();
                string[] signedNames = SaveIntegrity.SignedFieldNames(SaveData.CurrentVersion);
                foreach (System.Reflection.FieldInfo fi in typeof(SaveData).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    if (Array.IndexOf(signedNames, fi.Name) < 0 && Array.IndexOf(SaveIntegrity.ExcludedFields, fi.Name) < 0) missing.Add(fi.Name);
                Report.Check("[QA-06] every SaveData field is in SignedFields(v" + SaveData.CurrentVersion + ") or ExcludedFields (adding a field forces a decision)", missing.Count == 0, string.Join(",", missing.ToArray()));
                // mutate each signed field (keeping the stored hash): must be Tampered
                int survived = 0; string firstSurv = null;
                GameState g = RandomState(w, b, r); g.SetLock(new string[] { "howl", "johan" }); if (g.VisitedNodes.Count == 0) g.VisitedNodes.Add("N0"); g.Flags["k"] = 1;
                SaveData good = SaveMapper.ToSave(g, 1, "qa");
                sv.Save(1, good); string goodText = ms.Files["slot1.json"];
                Action<string, Action<SaveData>> mut = delegate (string name, Action<SaveData> f)
                {
                    SaveData m = ser.FromJson(goodText); f(m);
                    ms.Files["slot1.json"] = ser.ToJson(m); ms.Baks.Clear();
                    LoadStatus st = sv.Load(1).Status;
                    if (st == LoadStatus.Ok || st == LoadStatus.RestoredFromBackup) { survived++; if (firstSurv == null) firstSurv = name; }
                };
                mut("Slot", m => m.Slot = (m.Slot + 1) % 4);
                mut("SavedAtUtc", m => m.SavedAtUtc += "x"); mut("AppVersion", m => m.AppVersion += "x");
                mut("PlayerName", m => m.PlayerName += "x"); mut("Gender", m => m.Gender = m.Gender == "male" ? "female" : "male");
                mut("PlayerSpecies", m => m.PlayerSpecies = m.PlayerSpecies == "wolf" ? "fox" : "wolf"); mut("PlayerAppearance", m => m.PlayerAppearance = m.PlayerAppearance % 6 + 1);
                mut("Difficulty", m => m.Difficulty = (m.Difficulty + 1) % 3); mut("Chapter", m => m.Chapter = m.Chapter % 8 + 1);
                mut("Day", m => m.Day++); mut("DayInChapter", m => m.DayInChapter = m.DayInChapter % 50 + 1);
                mut("Gold", m => m.Gold += 0.5f); mut("Reputation", m => m.Reputation = (m.Reputation + 1f) % 100f); mut("Fatigue", m => m.Fatigue = (m.Fatigue + 1f) % 100f);
                mut("ShopLevel", m => m.ShopLevel = m.ShopLevel % 10 + 1); mut("SatisfactionBonus", m => m.SatisfactionBonus = m.SatisfactionBonus > 0 ? 0f : 0.03f);
                mut("LockedRoute", m => m.LockedRoute = "beomjun"); mut("LockedGroup", m => m.LockedGroup.Reverse());
                mut("PlayTimeSec", m => m.PlayTimeSec += 1.0);
                mut("Chars.Aff", m => m.Chars[0].Aff = (m.Chars[0].Aff + 1f) % 100f); mut("Chars.Trust", m => m.Chars[0].Trust = (m.Chars[0].Trust + 1f) % 50f);
                mut("Chars.EventsDone", m => m.Chars[0].EventsDone = (m.Chars[0].EventsDone + 1) % 6); mut("Chars.LastGiftDay", m => m.Chars[0].LastGiftDay++);
                mut("Chars.LastDateDay", m => m.Chars[0].LastDateDay++); mut("Chars.LastContactDay", m => m.Chars[0].LastContactDay++);
                mut("Chars.Id", m => m.Chars.Reverse()); mut("Chars.Remove", m => m.Chars.RemoveAt(0));
                mut("Flags.Value", m => m.Flags[0].Value += 1); mut("Flags.Add", m => m.Flags.Add(new FlagSave { Key = "evil", Value = 1 }));
                mut("VisitedNodes.Add", m => m.VisitedNodes.Add("Evil")); mut("VisitedNodes.Clear", m => m.VisitedNodes.Clear());
                mut("Version(1)", m => m.Version = 1);
                Report.Check("[QA-06] mutating any single signed field of a v2 save is rejected (no Ok / no silent .bak)", survived == 0, survived + " survived, first=" + firstSurv);
            }

            // chapter-transition mid-state: autosave happens at StartDay BEFORE Outro/Intro are played
            {
                GameState s = GameState.NewGame(b, Difficulty.Normal, "x");
                s.Chapter = 2; s.DayInChapter = 1; s.Day = 7; s.Phase = DayPhase.Morning;
                Func<string, bool> exists = delegate (string n2) { return w.Proj.Contains(n2); };
                HashSet<string> visited = new HashSet<string>();
                Func<string, bool> vis = delegate (string n2) { return visited.Contains(n2); };
                ChapterStep s1 = ChapterFlow.NextMorningStep(2, 1, false, exists, vis);
                bool hasOutro1 = w.Proj.Contains("Ch1_Outro"); // QA-13: the writer added Ch1_Outro; the rule is "Outro first when it exists, else Intro"
                Report.Check("ch2 day1 after load (nothing visited): Ch1_Outro first if the node exists, otherwise Intro", hasOutro1 ? (s1.Kind == ChapterStepKind.Outro && s1.Node == "Ch1_Outro") : (s1.Kind == ChapterStepKind.Intro && s1.Node == "Ch2_Intro"), s1.Kind + " " + s1.Node);
                if (hasOutro1) visited.Add("Ch1_Outro");
                visited.Add("Ch2_Intro");
                ChapterStep s2 = ChapterFlow.NextMorningStep(2, 1, false, exists, vis);
                Report.Check("after Outro/Intro visited and introPending=false -> none", s2.Kind == ChapterStepKind.None);
                ChapterStep s3 = ChapterFlow.NextMorningStep(3, 1, false, exists, vis);
                Report.Check("ch3 day1: Ch2_Outro first", s3.Kind == ChapterStepKind.Outro && s3.Node == "Ch2_Outro");
            }

            // flag-count / visited-count headroom vs validator limits (static: how many flags can scripts set?)
            HashSet<string> allFlags = new HashSet<string>();
            System.Text.RegularExpressions.Regex rx = new System.Text.RegularExpressions.Regex("<<flag\\s+\\$?(\\w+)");
            foreach (string f in System.IO.Directory.GetFiles(w.YarnDir, "*.yarn", System.IO.SearchOption.AllDirectories))
                foreach (System.Text.RegularExpressions.Match m in rx.Matches(System.IO.File.ReadAllText(f))) allFlags.Add(m.Groups[1].Value);
            Report.Info("  distinct flags settable by scripts: " + allFlags.Count + " (validator limit 1024), nodes " + w.Proj.AllNodes.Count + " (visited limit 4096)");
            Report.Check("flag headroom (validator limit 1024)", allFlags.Count < 800);

            // validator rejects weird locks
            {
                GameState s = GameState.NewGame(b, Difficulty.Normal, "x"); s.Phase = DayPhase.Morning;
                s.SetLock(new string[] { "howl", "howl" });
                string v = SaveValidator.Validate(SaveMapper.ToSave(s, 1, "qa"));
                Report.Check("validator rejects duplicate group members", v != null);
                s.SetLock(new string[] { "zzz" });
                v = SaveValidator.Validate(SaveMapper.ToSave(s, 1, "qa"));
                Report.Info("  validator on unknown lock id: " + (v ?? "ACCEPTED"));
                Report.Check("[PROBE] validator rejects lock on unknown character id 'zzz'", v != null, "accepted -> every real character becomes 'locked out' (frozen) and ending falls to community");
                s.SetLock(new string[] { "howl", "johan" });
                SaveData d = SaveMapper.ToSave(s, 1, "qa"); d.LockedRoute = "johan";
                Report.Check("validator rejects LockedRoute != LockedGroup[0]", SaveValidator.Validate(d) != null);
                d = SaveMapper.ToSave(s, 1, "qa"); d.Chars[0].Trust = 50.0001f;
                Report.Check("validator rejects trust > 50 (chapter 8 cap)", SaveValidator.Validate(d) != null);
            }

            // QA-15 (Data part): SetLock(state.LockedGroup) must not wipe the lock; QA-12: unparseable numeric cell throws
            {
                GameState g = GameState.NewGame(b, Difficulty.Normal, "x");
                g.SetLock(new string[] { "howl", "johan" });
                g.SetLock(g.LockedGroup);
                Report.Check("[QA-15] SetLock(state.LockedGroup) keeps the lock (aliasing)", g.LockedGroup.Count == 2 && g.LockedRoute == "howl");
                CsvTable t = CsvTable.Parse("a,b,c\n1,abc,\n");
                CsvRow row = t.Rows[0]; bool threw = false;
                try { row.Float("b"); } catch (FormatException) { threw = true; }
                Report.Check("[QA-12] non-numeric CSV cell throws, empty cell falls back, numeric parses", threw && row.Float("c", 7f) == 7f && row.Int("a") == 1);
            }

            // tamper -> backup fallback semantics: the caller must be told the primary was rejected
            {
                MemStorage ms = new MemStorage(); XmlSer ser = new XmlSer(); SaveService sv = new SaveService(ser, ms);
                GameState s = RandomState(w, b, r); s.Gold = 1000; sv.Save(1, SaveMapper.ToSave(s, 1, "qa"));
                s.Gold = 2000; sv.Save(1, SaveMapper.ToSave(s, 1, "qa"));
                ms.Files["slot1.json"] = ms.Files["slot1.json"].Replace("<Gold>2000</Gold>", "<Gold>999999</Gold>");
                LoadResult lr = sv.Load(1);
                Report.Info("  tampered primary + valid .bak => status " + lr.Status + " detail='" + lr.Detail + "' gold=" + (lr.Data != null ? lr.Data.Gold.ToString() : "-"));
                Report.Check("[QA-06] tampered primary + valid .bak: RestoredFromBackup, Detail 'primary: Tampered', PrimaryRejected set, bak data (1000) returned",
                    lr.Status == LoadStatus.RestoredFromBackup && lr.Detail == "primary: Tampered" && lr.PrimaryStatus == LoadStatus.Tampered && lr.PrimaryRejected && lr.Data != null && lr.Data.Gold == 1000f,
                    "status=" + lr.Status + " detail=" + lr.Detail + " rejected=" + lr.PrimaryRejected);
                ms.Files["slot1.json"] = "garbage";
                LoadResult lc = sv.Load(1);
                Report.Check("[QA-06] corrupt primary + valid .bak: RestoredFromBackup, Detail 'primary: Corrupt', PrimaryRejected", lc.Status == LoadStatus.RestoredFromBackup && lc.Detail == "primary: Corrupt" && lc.PrimaryRejected);
                ms.Files.Remove("slot1.json");
                LoadResult ln = sv.Load(1);
                Report.Check("[QA-06] missing primary + valid .bak: RestoredFromBackup but PrimaryRejected=false (nothing was tampered)", ln.Status == LoadStatus.RestoredFromBackup && !ln.PrimaryRejected);
                Report.Check("[QA-06] unsupported (future) version is Corrupt, not Ok", FutureVersion(ser, s) == LoadStatus.Corrupt);
            }
        }
    }

    public static class ListExt
    {
        public static bool SequenceEqualStr(this List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
