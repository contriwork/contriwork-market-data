/**
 * Port definition — see CONTRACT.md for the language-agnostic specification.
 * Method names on this interface mirror the Python (`snake_case`) and C#
 * (`PascalCaseAsync`) ports. Any signature change MUST land in CONTRACT.md
 * first and be mirrored in all three languages in the same PR.
 */
export interface PackageNamePort {
  /**
   * TODO: replace with a real contract method.
   *
   * @param input - Non-empty UTF-8 string, length <= 4096.
   * @returns A non-empty string derived deterministically from `input`.
   * @throws Error tagged with code `INVALID_INPUT` when validation fails.
   */
  example(input: string): Promise<string>;
}
