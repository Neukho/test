using System.Collections.Generic;

namespace MorpheHouse.Dialogue
{
    /// <summary>DialogueStep 의 종류.</summary>
    public enum StepKind
    {
        Line = 0,
        Choices = 1,
        Command = 2,
        End = 3
    }

    /// <summary>선택지 하나(UI 표시용). Available 이 false 면 비활성(잠금) 표시.</summary>
    public sealed class DialogueOption
    {
        /// <summary>Choose(index) 에 넘길 인덱스(비활성 포함 목록 내 위치).</summary>
        public int Index;
        /// <summary>보간·현지화가 끝난 표시 텍스트.</summary>
        public string Text;
        public bool Available;
        /// <summary>#line:xxxx 의 xxxx. 없으면 null.</summary>
        public string LineId;
        public List<string> Tags = new List<string>();
    }

    /// <summary>Next() 가 돌려주는 한 단계. Kind 에 따라 해당 필드만 의미가 있다.</summary>
    public sealed class DialogueStep
    {
        public StepKind Kind;

        // Line
        /// <summary>화자 id(소문자, 예: howl / player). 지문이면 null.</summary>
        public string Speaker;
        /// <summary>보간·현지화가 끝난 텍스트.</summary>
        public string Text;
        public string LineId;
        public List<string> Tags = new List<string>();

        // Choices
        public List<DialogueOption> Options = new List<DialogueOption>();

        // Command
        public string CommandName;
        public string[] Args = new string[0];

        /// <summary>원본 .yarn 줄 번호(디버그용).</summary>
        public int SourceLine;

        public bool IsLine { get { return Kind == StepKind.Line; } }
        public bool IsChoices { get { return Kind == StepKind.Choices; } }
        public bool IsCommand { get { return Kind == StepKind.Command; } }
        public bool IsEnd { get { return Kind == StepKind.End; } }

        public static DialogueStep MakeEnd()
        {
            DialogueStep s = new DialogueStep();
            s.Kind = StepKind.End;
            return s;
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case StepKind.Line: return (Speaker ?? "(narration)") + ": " + Text;
                case StepKind.Choices: return "Choices[" + Options.Count + "]";
                case StepKind.Command: return "Command " + CommandName + " " + string.Join(" ", Args);
                default: return "End";
            }
        }
    }
}
