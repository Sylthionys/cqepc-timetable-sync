# Timetable PDF Chinese Tokens

This file keeps the human-readable Chinese tokens referenced by the CQEPC timetable PDF parser so the main parser doc can stay ASCII-heavy when terminals or editors misread encodings.

## Core Layout Labels

- class header suffix: `课表`
- practical summary prefix: `实践课程`
- print timestamp prefix: `打印时间:`
- period lead example: `(1-2节)`

## Tagged Metadata Labels

- `/校区:`
- `/场地:`
- `/教师:`
- `/教学班组成:`
- `/教学班人数:`
- `/考核方式:`
- `/课程学时组成:`
- `/学分:`

## Course Type Markers

- `★` => theory
- `☆` => lab
- `◆` => practical
- `■` => computer
- `〇` => extracurricular

## Notes

- The runtime source of truth for parser tokens remains `TimetablePdfLexicon.cs`.
- This document is only a readable companion for parser documentation.

## 2026-04 Alias Notes

- `教学班:` must be treated as the short alias of `教学班组成:`.
- When the PDF uses `教师:.../教学班:...`, the parser must split those into:
  - `Teacher`
  - `TeachingClassComposition`
  - remaining tail metadata in `Notes`
- The parser must not leave `/教学班:...` attached to the teacher field.
