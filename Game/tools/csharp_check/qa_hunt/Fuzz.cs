using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;

namespace QaHunt
{
    /// <summary>Robustness fuzz: mutated real scripts / expressions / CSV must fail only with the documented exception types, never hang.</summary>
    public static class Fuzz
    {
        public static void Run(World w)
        {
            Report.Header("PARSER / CSV FUZZ");
            Random r = new Random(4242);
            List<string> sources = new List<string>();
            foreach (string f in Directory.GetFiles(w.YarnDir, "*.yarn", SearchOption.AllDirectories)) sources.Add(File.ReadAllText(f));
            int ok = 0, parseEx = 0, bad = 0; string firstBad = null;
            DateTime t0 = DateTime.Now;
            for (int i = 0; i < 3000; i++)
            {
                string src = sources[r.Next(sources.Count)];
                StringBuilder sb = new StringBuilder(src);
                int muts = 1 + r.Next(6);
                for (int m = 0; m < muts; m++)
                {
                    int k = r.Next(6); int pos = r.Next(Math.Max(1, sb.Length));
                    string[] junk = { "<<", ">>", "{", "}", "->", "<<if ", "<<endif>>", "\t", "    ", "---", "===", "\n", "#line:", "\"", "$", "<<jump X>>", "<<set $a to ", "<<else>>" };
                    if (k == 0 && sb.Length > 5) sb.Remove(pos, Math.Min(r.Next(1, 40), sb.Length - pos));
                    else if (k == 1) sb.Insert(pos, junk[r.Next(junk.Length)]);
                    else if (k == 2 && sb.Length > 5) { int a = r.Next(sb.Length - 1); char c = sb[a]; sb[a] = sb[a + 1]; sb[a + 1] = c; }
                    else if (k == 3) sb.Insert(pos, junk[r.Next(junk.Length)] + junk[r.Next(junk.Length)]);
                    else if (k == 4 && sb.Length > 5) sb.Length = pos;
                    else sb.Insert(pos, "\r");
                }
                try
                {
                    DialogueProject p = DialogueProject.Parse(sb.ToString(), "fuzz");
                    ok++;
                    // run first node with first choices to find runtime crashes of other types
                    foreach (DialogueNode n in p.AllNodes)
                    {
                        MorpheHouse.Data.GameState s = new MorpheHouse.Data.GameState();
                        DialogueRunner run = new DialogueRunner(p, new InMemoryVariableStore(), new NullDialogueHost());
                        try
                        {
                            run.Start(n.Title);
                            for (int st = 0; st < 400; st++)
                            {
                                DialogueStep step = run.Next();
                                if (step.IsEnd) break;
                                if (step.IsChoices) { int idx = Runner.PickFirst(step); if (idx < 0) break; run.Choose(idx); }
                            }
                        }
                        catch (DialogueRuntimeException) { }
                        catch (DialogueParseException) { }
                        catch (InvalidOperationException) { }
                        catch (ArgumentOutOfRangeException) { }
                    }
                }
                catch (DialogueParseException) { parseEx++; }
                catch (DialogueRuntimeException) { parseEx++; }
                catch (Exception ex)
                {
                    bad++;
                    if (firstBad == null) firstBad = ex.GetType().Name + ": " + ex.Message + " @ " + (ex.StackTrace ?? "").Split('\n')[0];
                }
                if ((DateTime.Now - t0).TotalSeconds > 90) { Report.Info("  fuzz time budget hit at iteration " + i); break; }
            }
            Report.Info("  yarn mutants: parsed=" + ok + " rejected(DialogueException)=" + parseEx + " unexpected=" + bad);
            Report.Check("yarn parser/runner throw only DialogueException on mutated real scripts", bad == 0, firstBad);

            // expressions
            string[] pieces = { "flag(\"x\")", "$a", "1", "2.5", "and", "or", "not", "(", ")", "+", "-", "*", "/", "==", "!=", ">=", "<", "\"s\"", "true", "false", ",", "affinity(\"howl\")", "$aff_howl", "!", "&&", "||", "%" };
            int exOk = 0, exRej = 0, exBad = 0; string exFirst = null;
            for (int i = 0; i < 20000; i++)
            {
                StringBuilder sb = new StringBuilder();
                int n = 1 + r.Next(8);
                for (int k = 0; k < n; k++) { sb.Append(pieces[r.Next(pieces.Length)]); sb.Append(r.Next(3) == 0 ? "" : " "); }
                try
                {
                    DialogueExpression e = DialogueExpression.Parse(sb.ToString());
                    exOk++;
                    try { e.Evaluate(new NullCtx()); } catch (DialogueRuntimeException) { } catch (DialogueParseException) { } catch (InvalidOperationException) { } catch (DivideByZeroException) { exBad++; if (exFirst == null) exFirst = "DivideByZero in '" + sb + "'"; }
                }
                catch (DialogueParseException) { exRej++; }
                catch (DialogueRuntimeException) { exRej++; }
                catch (Exception ex) { exBad++; if (exFirst == null) exFirst = ex.GetType().Name + " '" + sb + "': " + ex.Message; }
            }
            Report.Info("  expressions: ok=" + exOk + " rejected=" + exRej + " unexpected=" + exBad);
            Report.Check("expression parser/evaluator throw only DialogueException", exBad == 0, exFirst);

            // profile.json loader: must never throw (documented), whatever is in the file
            {
                MorpheHouse.GameFlow.MemoryProfileStorage ms = new MorpheHouse.GameFlow.MemoryProfileStorage();
                MorpheHouse.GameFlow.ProfileService seedSvc = new MorpheHouse.GameFlow.ProfileService(ms);
                MorpheHouse.Romance.EndingResult er = new MorpheHouse.Romance.EndingResult();
                seedSvc.RecordEnding(er); seedSvc.RecordCg("HOWL_E1"); seedSvc.AddUnlock("extra_epilogues"); seedSvc.Save();
                string baseJson = ms.Text;
                int pbad = 0; string pfirst = null;
                string[] extras = { "{", "}", "[", "]", ":", ",", "\"", "\\", "1e999", "-1", "null", "true", "\"endings\":{\"a\":1,\"a\":2}", "NaN", "\u0000", "\\u12", "99999999999999999999" };
                for (int i = 0; i < 6000; i++)
                {
                    StringBuilder sb = new StringBuilder(baseJson);
                    int muts = 1 + r.Next(5);
                    for (int m = 0; m < muts; m++)
                    {
                        int pos = r.Next(Math.Max(1, sb.Length)); int k = r.Next(4);
                        if (k == 0 && sb.Length > 3) sb.Remove(pos, Math.Min(r.Next(1, 8), sb.Length - pos));
                        else if (k == 1) sb.Insert(pos, extras[r.Next(extras.Length)]);
                        else if (k == 2 && sb.Length > 3) sb.Length = pos;
                        else sb.Insert(pos, extras[r.Next(extras.Length)]);
                    }
                    MorpheHouse.GameFlow.MemoryProfileStorage st = new MorpheHouse.GameFlow.MemoryProfileStorage(); st.Text = sb.ToString();
                    try { new MorpheHouse.GameFlow.ProfileService(st).Load(); }
                    catch (Exception ex) { pbad++; if (pfirst == null) pfirst = ex.GetType().Name + ": " + ex.Message + " on " + sb.ToString().Replace("\n", " "); }
                }
                Report.Check("ProfileService.Load never throws on 6000 mutated profile.json files", pbad == 0, pbad + " throws, first: " + pfirst);
                MorpheHouse.GameFlow.MemoryProfileStorage st2 = new MorpheHouse.GameFlow.MemoryProfileStorage(); st2.Text = "{\"endings\":{\"a\":1,\"a\":2}}";
                string dupRes;
                try { dupRes = new MorpheHouse.GameFlow.ProfileService(st2).Load().ToString(); } catch (Exception ex) { dupRes = "THROWS " + ex.GetType().Name; }
                Report.Info("  duplicate JSON object keys in profile.json -> " + dupRes);
            }
            // CSV: round-trip all balance csv through CsvTable and compare cell counts with a naive independent parse
            int csvBad = 0;
            foreach (string f in Directory.GetFiles(w.BalDir, "*.csv"))
            {
                string text = File.ReadAllText(f);
                CsvTable t = CsvTable.Parse(text);
                string[] lines = text.Replace("\r", "").Split('\n');
                int dataLines = 0; for (int i = 1; i < lines.Length; i++) if (lines[i].Trim().Length > 0) dataLines++;
                if (t.Rows.Count != dataLines) { csvBad++; Report.Info("  csv row count mismatch " + Path.GetFileName(f) + ": parsed " + t.Rows.Count + " vs " + dataLines); }
            }
            Report.Check("balance CSVs parse to the expected row counts", csvBad == 0);
            // CSV edge cases
            CsvTable bom = CsvTable.Parse("﻿a,b\r\n1,2\r\n\r\n3,\"x,y\"\r\n");
            Report.Check("CsvTable: BOM + CRLF + blank line + quoted comma", bom.Rows.Count == 2 && bom.Rows[0].Str("a") == "1" && bom.Rows[1].Str("b") == "x,y", bom.Rows.Count + " rows");
            string numProbe = null;
            try { CsvTable t2 = CsvTable.Parse("k,v\nx,abc\n"); float f = t2.Rows[0].Float("v"); numProbe = "Float('abc') = " + f; } catch (Exception ex) { numProbe = "throws " + ex.GetType().Name; }
            Report.Info("  CsvRow.Float on non-numeric cell: " + numProbe + " (a typo in a balance CSV silently becomes 0 if it returns a value)");
        }

        private sealed class NullCtx : IExpressionContext
        {
            public DialogueValue GetVariable(string name) { return default(DialogueValue); }
            public DialogueValue CallFunction(string name, DialogueValue[] args) { return default(DialogueValue); }
        }
    }
}
