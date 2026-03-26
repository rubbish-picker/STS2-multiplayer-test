"""
LLM 流式调用封装：统一处理 claude_subscription（CLI）和 api_key（litellm）两种模式。
"""
from __future__ import annotations

import asyncio
import subprocess
from typing import AsyncIterator, Callable, Awaitable

import litellm

_MODEL_MAP = {
    "anthropic": "claude-sonnet-4-6",
    "moonshot":  "moonshot/moonshot-v1-8k",
    "deepseek":  "deepseek/deepseek-chat",
    "qwen":      "openai/qwen-plus",
    "zhipu":     "zhipuai/glm-4-flash",
    "openai":    "gpt-4.1-mini",
}


async def stream_analysis(
    system_prompt: str,
    user_prompt: str,
    llm_cfg: dict,
    on_chunk: Callable[[str], Awaitable[None]],
) -> str:
    """
    调用 LLM 进行分析，流式回调每个文本片段。

    - claude_subscription 模式：通过 Claude Code CLI 获取完整结果，
      然后模拟流式（每 80 字符一批）发给前端，保持 UI 体验一致。
    - api_key 模式：litellm 真正流式。

    返回完整文本。
    """
    if llm_cfg.get("mode") == "claude_subscription":
        return await _stream_via_cli(system_prompt, user_prompt, on_chunk)
    else:
        return await _stream_via_litellm(system_prompt, user_prompt, llm_cfg, on_chunk)


async def _stream_via_cli(
    system_prompt: str,
    user_prompt: str,
    on_chunk: Callable[[str], Awaitable[None]],
) -> str:
    combined = f"{system_prompt}\n\n{user_prompt}"
    loop = asyncio.get_event_loop()
    result = await asyncio.wait_for(
        loop.run_in_executor(
            None,
            lambda: subprocess.run(
                ["claude", "--print", "-p", combined],
                capture_output=True, timeout=180,
            ),
        ),
        timeout=185,
    )
    full_text = result.stdout.decode("utf-8", errors="replace").strip()

    # 模拟流式：分批发送，让前端光标动起来
    CHUNK = 80
    for i in range(0, len(full_text), CHUNK):
        await on_chunk(full_text[i:i + CHUNK])
        await asyncio.sleep(0)   # 让 event loop 有机会处理其他任务

    return full_text


async def _stream_via_litellm(
    system_prompt: str,
    user_prompt: str,
    llm_cfg: dict,
    on_chunk: Callable[[str], Awaitable[None]],
) -> str:
    provider = llm_cfg.get("provider", "anthropic")
    model = llm_cfg.get("model") or _MODEL_MAP.get(provider, "claude-sonnet-4-6")

    stream = await litellm.acompletion(
        model=model,
        messages=[
            {"role": "system", "content": system_prompt},
            {"role": "user",   "content": user_prompt},
        ],
        api_key=llm_cfg.get("api_key") or None,
        api_base=llm_cfg.get("base_url") or None,
        temperature=0.2,
        max_tokens=2048,
        stream=True,
    )

    full_text: list[str] = []
    async for chunk in stream:
        delta = chunk.choices[0].delta.content or ""
        if delta:
            full_text.append(delta)
            await on_chunk(delta)

    return "".join(full_text)
