"""Versioned prompt templates stored as files under app/ai/prompts/<name>/<version>.md."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

PROMPT_ROOT = Path(__file__).resolve().parent / "prompts"


@dataclass(frozen=True)
class PromptTemplate:
    name: str
    version: str
    text: str

    @property
    def id(self) -> str:
        return f"{self.name}-{self.version}"

    def render(self, **values: str) -> str:
        return self.text.format(**values)


def load_prompt(name: str, version: str = "v1") -> PromptTemplate:
    path = PROMPT_ROOT / name / f"{version}.md"
    return PromptTemplate(name=name, version=version, text=path.read_text(encoding="utf-8"))
