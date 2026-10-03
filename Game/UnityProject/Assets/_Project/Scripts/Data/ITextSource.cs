using System.IO;

namespace MorpheHouse.Data
{
    /// <summary>이름으로 텍스트(CSV/JSON)를 읽어 오는 추상화. Unity(Resources)와 일반 .NET(파일) 양쪽에서 구현한다.</summary>
    public interface ITextSource
    {
        /// <summary>텍스트를 반환한다. 없으면 null.</summary>
        string Load(string name);
    }

    /// <summary>디렉터리의 {name}.csv 파일을 읽는 구현(테스트·CLI 검증용).</summary>
    public sealed class FileTextSource : ITextSource
    {
        private readonly string _directory;

        public FileTextSource(string directory)
        {
            _directory = directory;
        }

        public string Load(string name)
        {
            string path = Path.Combine(_directory, name + ".csv");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }
}
