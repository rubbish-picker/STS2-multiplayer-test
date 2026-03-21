import { useEffect, useRef } from "react";
import { cn } from "../lib/utils";

interface Props {
  lines: string[];
  className?: string;
}

export function AgentLog({ lines, className }: Props) {
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [lines]);

  return (
    <div
      className={cn(
        "bg-slate-50 border border-slate-200 rounded-lg p-3 font-mono text-xs text-slate-600 overflow-y-auto max-h-64",
        className
      )}
    >
      {lines.map((line, i) => (
        <div key={i} className="whitespace-pre-wrap leading-5">{line}</div>
      ))}
      <div ref={bottomRef} />
    </div>
  );
}
