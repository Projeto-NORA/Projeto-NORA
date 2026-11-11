package com.example.app_nora.api

/** Checks formatting and check digits only; it does not verify identity or Receita status. */
fun isValidCpf(value: String): Boolean {
    if (value.any { it !in '0'..'9' && it !in ".- " }) return false
    val digits = value.filter { it in '0'..'9' }
    if (digits.length != 11 || digits.all { it == digits[0] }) return false
    for (count in 9..10) {
        val sum = (0 until count).sumOf { (digits[it] - '0') * (count + 1 - it) }
        val digit = (sum * 10 % 11).let { if (it == 10) 0 else it }
        if (digits[count] - '0' != digit) return false
    }
    return true
}
