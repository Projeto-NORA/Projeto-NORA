package com.example.app_nora

import com.example.app_nora.api.isValidCpf
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class CpfValidationTest {
    @Test fun acceptsValidDigitsWithOrWithoutFormatting() {
        assertTrue(isValidCpf("52998224725"))
        assertTrue(isValidCpf("529.982.247-25"))
    }

    @Test fun rejectsInvalidDigitsRepeatedNumbersAndUnexpectedCharacters() {
        assertFalse(isValidCpf("52998224724"))
        assertFalse(isValidCpf("111.111.111-11"))
        assertFalse(isValidCpf("5299822472"))
        assertFalse(isValidCpf("52998224725x"))
        assertFalse(isValidCpf(""))
    }
}
