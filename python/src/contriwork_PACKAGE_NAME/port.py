"""Port definition — see CONTRACT.md for the language-agnostic specification."""

from __future__ import annotations

from typing import Protocol, runtime_checkable


@runtime_checkable
class PackageNamePort(Protocol):
    """Placeholder port. Replace with the real contract methods.

    Keep method names aligned with C# (`PascalCaseAsync`) and TypeScript
    (`camelCase`) implementations. Any signature change here MUST land in
    CONTRACT.md first and be mirrored in all three languages in the same PR.
    """

    async def example(self, input: str) -> str:
        """TODO: replace with a real contract method.

        Args:
            input: Non-empty UTF-8 string, length <= 4096.

        Returns:
            A non-empty string derived deterministically from ``input``.

        Raises:
            ValueError: If ``input`` fails validation (error code
                ``INVALID_INPUT`` per CONTRACT.md).
        """
        ...
