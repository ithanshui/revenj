package org.revenj.postgres.converters;

public abstract class NumberConverter {

	private static final int[] NUMBERS;

	static {
		NUMBERS = new int[100];
		for (int i = 0; i < NUMBERS.length; i++) {
			char first = (char) ((i / 10) + '0');
			char second = (char) ((i % 10) + '0');
			short offset = i < 10 ? (short) 1 : (short) 0;
			NUMBERS[i] = (offset << 24) + (first << 8) + second;
		}
	}

	static void write2(int number, char[] buffer, int start) {
		int pair = NUMBERS[number];
		buffer[start] = (char) (byte) (pair);
		buffer[start + 1] = (char) (pair >> 8);
	}

	static void write4(int number, char[] buffer, int start) {
		int div = number / 100;
		int pair1 = NUMBERS[div];
		buffer[start] = (char) (byte) (pair1);
		buffer[start + 1] = (char) (pair1 >> 8);
		int rem = number - div * 100;
		int pair2 = NUMBERS[rem];
		buffer[start + 2] = (char) (byte) (pair2);
		buffer[start + 3] = (char) (pair2 >> 8);
	}

	static int read2(char[] source, int start) {
		int first = source[start] - 48;
		return (first << 3) + (first << 1) + source[start + 1] - 48;
	}

	static int read4(char[] source, int start) {
		int first = source[start] - 48;
		int second = source[start + 1] - 48;
		int third = source[start + 2] - 48;
		return first * 1000 + second * 100 + (third << 3) + (third << 1) + source[start + 3] - 48;
	}

	public static Integer tryParsePositiveInt(String number) {
		if (number.length() == 0 || number.charAt(0) < '0' || number.charAt(0) > '9') {
			return null;
		}
		int value = 0;
		for (int i = 0; i < number.length(); i++)
			value = (value << 3) + (value << 1) + number.charAt(i) - '0';
		return value;
	}

	public static long parseLong(String number) {
		long value = 0;
		if (number.charAt(0) == '-') {
			for (int i = 1; i < number.length(); i++)
				value = (value << 3) + (value << 2) - number.charAt(i) + '0';
		} else {
			for (int i = 0; i < number.length(); i++)
				value = (value << 3) + (value << 2) + number.charAt(i) - '0';
		}
		return value;
	}

	public static int serialize(int value, char[] buf, int start) {
		if (value == Integer.MIN_VALUE) {
			"-2147483648".getChars(0, 11, buf, start);
			return start + 11;
		}
		int q, r;
		int charPos = 10;
		int i;
		if (value < 0) {
			i = -value;
			buf[start] = '-';
			start += 1;
		} else {
			i = value;
		}
		int v = 0;
		while (charPos > 1) {
			q = i / 100;
			r = i - ((q << 6) + (q << 5) + (q << 2));
			i = q;
			v = NUMBERS[r];
			buf[charPos--] = (char)(byte) v;
			buf[charPos--] = (char) (v >> 8);
			if (i == 0) break;
		}
		int len = charPos + 1 + (v >> 16);
		for (int x = start; x < start + 11 - len; x++) {
			buf[x] = buf[x + charPos];
		}
		return start + 11 - len;
	}

	public static int serialize(long value, char[] buf, int start) {
		if (value == Long.MIN_VALUE) {
			"-9223372036854775808".getChars(0, 21, buf, start);
			return start + 11;
		}
		long q;
		int r;
		int charPos = 20;
		long i;
		if (value < 0) {
			i = -value;
			buf[start] = '-';
			start += 1;
		} else {
			i = value;
		}

		int v = 0;
		while (charPos > 1) {
			q = i / 100;
			r = (int) (i - ((q << 6) + (q << 5) + (q << 2)));
			i = q;
			v = NUMBERS[r];
			buf[charPos--] = (char)(byte) v;
			buf[charPos--] = (char) (v >> 8);
			if (i == 0) break;
		}
		int len = charPos + 1 + (v >> 16);
		for (int x = start; x < start + 11 - len; x++) {
			buf[x] = buf[x + charPos];
		}
		return start + 11 - len;
	}
}