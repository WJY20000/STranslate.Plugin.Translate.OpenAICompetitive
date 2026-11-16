using System.IO;
using System.Text;

namespace STranslate.Plugin.Translate.OpenAICompetitive.Protocol;

// 标签可能跨多个 token 返回；只暂存可能构成标签的后缀。
internal sealed class ThinkingFilter
{
    private string _pending = string.Empty;
    private bool _thinking;
    private bool _trimAfterThinking;
    public string Append(string value)
    {
        _pending += value;
        var output = new StringBuilder();
        while (_pending.Length > 0)
        {
            var tag = _thinking ? "</think>" : "<think>";
            var index = _pending.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                if (!_thinking) Emit(output, _pending[..index]);
                _pending = _pending[(index + tag.Length)..];
                _thinking = !_thinking;
                if (!_thinking) _trimAfterThinking = true;
                continue;
            }
            var keep = Math.Min(tag.Length - 1, _pending.Length);
            while (keep > 0 && !tag.StartsWith(_pending[^keep..], StringComparison.OrdinalIgnoreCase)) keep--;
            if (!_thinking) Emit(output, _pending[..(_pending.Length - keep)]);
            _pending = keep == 0 ? string.Empty : _pending[^keep..];
            break;
        }
        return output.ToString();
    }

    public string Complete()
    {
        var output = new StringBuilder();
        if (_thinking) throw new InvalidDataException("推理内容尚未结束，服务端已中断响应。");
        Emit(output, _pending);
        _pending = string.Empty;
        return output.ToString();
    }
    private void Emit(StringBuilder output, string value)
    {
        if (_trimAfterThinking)
        {
            value = value.TrimStart();
            if (value.Length > 0) _trimAfterThinking = false;
        }
        output.Append(value);
    }
}
