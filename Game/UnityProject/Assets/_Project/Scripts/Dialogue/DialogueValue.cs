using System;
using System.Globalization;

namespace MorpheHouse.Dialogue
{
    /// <summary>대화 스크립트 값의 종류.</summary>
    public enum DialogueValueKind
    {
        Number = 0,
        String = 1,
        Bool = 2
    }

    /// <summary>
    /// 대화 스크립트의 값(숫자/문자열/불). 작은 태그드 유니온 구조체.
    /// default(DialogueValue) 는 숫자 0 이며, 정의되지 않은 변수의 값으로 쓰인다.
    /// </summary>
    public struct DialogueValue : IEquatable<DialogueValue>
    {
        private readonly DialogueValueKind _kind;
        private readonly double _number;
        private readonly string _string;
        private readonly bool _bool;

        private DialogueValue(DialogueValueKind kind, double number, string str, bool boolean)
        {
            _kind = kind;
            _number = number;
            _string = str;
            _bool = boolean;
        }

        /// <summary>값의 종류.</summary>
        public DialogueValueKind Kind { get { return _kind; } }

        public bool IsNumber { get { return _kind == DialogueValueKind.Number; } }
        public bool IsString { get { return _kind == DialogueValueKind.String; } }
        public bool IsBool { get { return _kind == DialogueValueKind.Bool; } }

        /// <summary>숫자 값. 불은 1/0 으로 변환, 문자열이면 예외.</summary>
        public double AsNumber
        {
            get
            {
                if (_kind == DialogueValueKind.Number) return _number;
                if (_kind == DialogueValueKind.Bool) return _bool ? 1.0 : 0.0;
                throw new DialogueRuntimeException("문자열 \"" + _string + "\" 을(를) 숫자로 쓸 수 없습니다.");
            }
        }

        /// <summary>문자열 표현(보간에 사용). 정수형 숫자는 소수점 없이, 불은 True/False.</summary>
        public string AsString
        {
            get { return ToString(); }
        }

        /// <summary>조건식에서의 참/거짓. 숫자는 0 이 아니면 참, 문자열은 비어 있지 않으면 참.</summary>
        public bool IsTruthy
        {
            get
            {
                switch (_kind)
                {
                    case DialogueValueKind.Bool: return _bool;
                    case DialogueValueKind.Number: return _number != 0.0;
                    default: return !string.IsNullOrEmpty(_string);
                }
            }
        }

        public static DialogueValue FromNumber(double value)
        {
            return new DialogueValue(DialogueValueKind.Number, value, null, false);
        }

        public static DialogueValue FromString(string value)
        {
            return new DialogueValue(DialogueValueKind.String, 0.0, value ?? string.Empty, false);
        }

        public static DialogueValue FromBool(bool value)
        {
            return new DialogueValue(DialogueValueKind.Bool, 0.0, null, value);
        }

        public override string ToString()
        {
            switch (_kind)
            {
                case DialogueValueKind.Bool:
                    return _bool ? "True" : "False";
                case DialogueValueKind.String:
                    return _string ?? string.Empty;
                default:
                    if (_number == Math.Floor(_number) && Math.Abs(_number) < 1e15)
                        return ((long)_number).ToString(CultureInfo.InvariantCulture);
                    return _number.ToString("R", CultureInfo.InvariantCulture);
            }
        }

        public bool Equals(DialogueValue other)
        {
            return DialogueOps.AreEqual(this, other);
        }

        public override bool Equals(object obj)
        {
            return obj is DialogueValue && Equals((DialogueValue)obj);
        }

        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }
    }
}
