using System.IO;
using System.Text;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.Tycoon;
using UnityEditor;
using UnityEngine;

namespace MorpheHouse.EditorTools
{
    /// <summary>밸런스 CSV 동기화와 자동 플레이 리포트.</summary>
    public static class BalanceTools
    {
        private const string Target = "Assets/_Project/Resources/Balance";

        /// <summary>저장소의 balance/*.csv 를 Resources/Balance 로 복사한다(CSV 를 고친 뒤 실행).</summary>
        [MenuItem("MorpheHouse/Data/Sync Balance CSV")]
        public static void Sync()
        {
            string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../balance"));
            if (!Directory.Exists(source))
            {
                Debug.LogError("[MorpheHouse] balance 폴더를 찾을 수 없습니다: " + source);
                return;
            }
            Directory.CreateDirectory(Target);
            int count = 0;
            foreach (string file in Directory.GetFiles(source, "*.csv"))
            {
                File.Copy(file, Path.Combine(Target, Path.GetFileName(file)), true);
                count++;
            }
            AssetDatabase.Refresh();
            Debug.Log("[MorpheHouse] 밸런스 CSV " + count + "개 동기화 완료.");
        }

        /// <summary>난이도별 8챕터 자동 완주 결과를 콘솔에 출력한다(Python 리포트와 비교).</summary>
        [MenuItem("MorpheHouse/Balance/Run AutoPlay Report")]
        public static void RunReport()
        {
            FileTextSource source = new FileTextSource(Target);
            StringBuilder sb = new StringBuilder("[MorpheHouse] AutoPlay 리포트 (경영만, 200회)\n");
            Difficulty[] all = { Difficulty.Story, Difficulty.Normal, Difficulty.Hard };
            for (int i = 0; i < all.Length; i++)
            {
                BalanceTable b = BalanceTable.Load(source, all[i]);
                AutoPlayer.Summary s = AutoPlayer.Run(b, 200, 1234);
                sb.Append(all[i]).Append("  일수 ").Append(s.Days).Append("  최종 골드 ").Append(s.FinalGold.ToString("F0"))
                  .Append("  Lv ").Append(s.FinalLevel.ToString("F1")).Append("  평판 ").Append(s.FinalReputation.ToString("F0")).Append('\n');
                sb.Append("  챕터별 일 평균 매출:");
                for (int c = 0; c < s.ChapterAverageGross.Length; c++) sb.Append(' ').Append(s.ChapterAverageGross[c].ToString("F0"));
                sb.Append('\n');
            }
            Debug.Log(sb.ToString());
        }
    }
}
