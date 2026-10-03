using System;

namespace MorpheHouse.Dialogue
{
    /// <summary>.yarn 텍스트나 표현식 구문 오류. 줄 번호(1부터, 모르면 0)를 포함한다.</summary>
    public sealed class DialogueParseException : Exception
    {
        /// <summary>오류가 난 줄 번호(1부터). 알 수 없으면 0.</summary>
        public readonly int LineNumber;

        /// <summary>스크립트 출처 이름(파일명 등). 없으면 빈 문자열.</summary>
        public readonly string SourceName;

        /// <summary>줄 번호·출처가 붙기 전의 순수 메시지.</summary>
        public readonly string RawMessage;

        public DialogueParseException(string sourceName, int lineNumber, string message)
            : base(Format(sourceName, lineNumber, message))
        {
            SourceName = sourceName ?? string.Empty;
            LineNumber = lineNumber;
            RawMessage = message;
        }

        private static string Format(string sourceName, int lineNumber, string message)
        {
            string src = sourceName ?? string.Empty;
            if (lineNumber > 0) return src + "(" + lineNumber + "): " + message;
            return src.Length > 0 ? src + ": " + message : message;
        }
    }

    /// <summary>실행 중 오류(정의되지 않은 함수·노드, 타입 불일치, 0으로 나누기 등).</summary>
    public sealed class DialogueRuntimeException : Exception
    {
        public DialogueRuntimeException(string message) : base(message) { }

        public DialogueRuntimeException(string message, Exception inner) : base(message, inner) { }
    }
}
