# Parser Audit TODO

## Completed Phases

### Phase 1: Core Tests (86 items) - COMPLETED

All basic SQL features tested: SELECT, INSERT, UPDATE, DELETE, DDL, expressions, etc.

### Phase 2: Grammar Extensions (27 items) - COMPLETED

- [x] EXISTS / NOT EXISTS expressions
- [x] Parameter placeholders (@, :, ?, $n)
- [x] CTE (WITH, WITH RECURSIVE)
- [x] Set operations (UNION, INTERSECT, EXCEPT)
- [x] Transaction statements

### Phase 3: EF Core Critical Features (14 items) - COMPLETED

- [x] RETURNING clause for INSERT/UPDATE/DELETE
- [x] Date functions (YEAR, MONTH, DAY, HOUR, MINUTE, SECOND)
- [x] LAST_INSERT_ROWID, IFNULL, TYPEOF

---

## Phase 4: Full WitSQL Specification Support

### String Functions (Priority: High)
- [x] 1. Add LEFT function to grammar and tests
- [x] 2. Add RIGHT function to grammar and tests
- [x] 3. Add LTRIM function to grammar and tests
- [x] 4. Add RTRIM function to grammar and tests
- [x] 5. Add CONCAT function to grammar and tests
- [x] 6. Add CONCAT_WS function to grammar and tests
- [x] 7. Add INSTR function to grammar and tests
- [ ] 8. Add POSITION function to grammar and tests
- [x] 9. Add REVERSE function to grammar and tests
- [x] 10. Add REPEAT function to grammar and tests
- [x] 11. Add SPACE function to grammar and tests
- [x] 12. Add LPAD function to grammar and tests
- [x] 13. Add RPAD function to grammar and tests
- [x] 14. Add SUBSTRING function (alias for SUBSTR)
- [x] 15. Add CHAR_LENGTH function
- [x] 16. Add OCTET_LENGTH function
- [ ] 17. Add FORMAT function

### Numeric Functions (Priority: Medium)
- [x] 18. Add SIGN function
- [x] 19. Add TRUNC function
- [x] 20. Add MOD function
- [x] 21. Add POWER function
- [x] 22. Add SQRT function
- [x] 23. Add EXP function
- [x] 24. Add LOG function
- [x] 25. Add LOG10 function
- [x] 26. Add LOG2 function
- [x] 27. Add SIN, COS, TAN functions
- [x] 28. Add ASIN, ACOS, ATAN functions
- [x] 29. Add ATAN2 function
- [x] 30. Add PI function
- [x] 31. Add DEGREES function
- [x] 32. Add RADIANS function
- [x] 33. Add RANDOM function
- [x] 34. Add CEILING function (alias for CEIL)

### Date/Time Functions (Priority: High)
- [x] 35. Add DAYOFWEEK function
- [x] 36. Add DAYOFYEAR function
- [x] 37. Add WEEKOFYEAR function
- [x] 38. Add QUARTER function
- [x] 39. Add DATEADD function
- [x] 40. Add DATEDIFF function
- [x] 41. Add STRFTIME function
- [x] 42. Add MAKEDATE function
- [x] 43. Add MAKETIME function

### Conversion Functions (Priority: Medium)
- [x] 44. Add CONVERT function (special syntax like CAST)
- [ ] 45. Add TOSTRING function
- [ ] 46. Add TOINT function
- [ ] 47. Add TODOUBLE function
- [ ] 48. Add TODECIMAL function
- [ ] 49. Add TOBOOLEAN function
- [ ] 50. Add TODATE function
- [ ] 51. Add TODATETIME function
- [ ] 52. Add TOGUID function
- [x] 53. Add HEX function
- [x] 54. Add UNHEX function
- [ ] 55. Add BASE64 function
- [ ] 56. Add UNBASE64 function

### Aggregate Functions (Priority: Medium)
- [x] 57. Add GROUP_CONCAT function

### Null Handling Functions (Priority: High)
- [x] 58. Add NVL function (alias for IFNULL)

### System Functions (Priority: Medium)
- [x] 59. Add DATABASE function
- [x] 60. Add VERSION function
- [x] 61. Add CHANGES function
- [ ] 62. Add ROWID pseudo-column support

### Window Functions (Priority: Medium)
- [x] 63. Add NTILE function
- [x] 64. Add PERCENT_RANK function
- [x] 65. Add CUME_DIST function
- [ ] 66. Add FIRST_VALUE function (token conflict - needs fix)
- [ ] 67. Add LAST_VALUE function (token conflict - needs fix)
- [ ] 68. Add NTH_VALUE function (token conflict - needs fix)
- [ ] 69. Add window frame clause support (ROWS/RANGE BETWEEN)

### ID Generation Functions (Priority: Low)
- [x] 70. Add NEWUUID function (alias for NEWGUID)
- [x] 71. Add LASTINCREMENT function

---

## Progress Tracking

| Phase  | Items | Completed | Status      |
|--------|-------|-----------|-------------|
| Phase 1| 86    | 86        | ? Done     |
| Phase 2| 27    | 27        | ? Done     |
| Phase 3| 14    | 14        | ? Done     |
| Phase 4| 71    | 55        | ? In Progress|
| **Total**  | **198** | **182**   | **92%**       |

**Total Tests:** 276

### Remaining Items (16):
1. POSITION function
2. FORMAT function  
3. TOSTRING, TOINT, TODOUBLE, TODECIMAL, TOBOOLEAN, TODATE, TODATETIME, TOGUID functions (8)
4. BASE64, UNBASE64 functions (2)
5. ROWID pseudo-column
6. FIRST_VALUE, LAST_VALUE, NTH_VALUE window functions (3) - token conflicts
7. Window frame clause (ROWS/RANGE BETWEEN)

---

**Last Updated:** 2024-12-19
