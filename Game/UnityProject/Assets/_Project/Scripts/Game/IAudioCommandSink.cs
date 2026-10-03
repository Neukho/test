namespace MorpheHouse.GameFlow
{
    /// <summary>
    /// 대사 커맨드 &lt;&lt;bgm id&gt;&gt; &lt;&lt;bgm_stop&gt;&gt; &lt;&lt;se id&gt;&gt; 를 받는 쪽(오디오 서비스). 순수 디스패처는 이 인터페이스로만 소리를 낸다.
    /// 클립 파일이 없어도 오류가 되면 안 된다 — 구현체는 조용히 무시한다. 알 수 없는 id 의 경고는 디스패처가 <see cref="HasAudio"/> 로 판정해 남긴다.
    /// </summary>
    public interface IAudioCommandSink
    {
        /// <summary>카탈로그에 이 id 가 이 종류로 등록되어 있는가(파일 존재 여부와 무관).</summary>
        bool HasAudio(string id, AudioKind kind);

        void PlayBgm(string id);

        void StopBgm();

        void PlaySe(string id);
    }

    /// <summary>아무것도 하지 않는 구현(오디오가 없는 환경·테스트).</summary>
    public sealed class NullAudioSink : IAudioCommandSink
    {
        public bool HasAudio(string id, AudioKind kind) { return false; }

        public void PlayBgm(string id) { }

        public void StopBgm() { }

        public void PlaySe(string id) { }
    }
}
