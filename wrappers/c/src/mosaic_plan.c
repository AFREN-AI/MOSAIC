#include "mosaic_plan.h"

#include <stdint.h>
#include <string.h>

static size_t escaped_length(const char *value) {
    size_t length = 0;
    for (const unsigned char *cursor = (const unsigned char *)value; *cursor; cursor++) {
        if (*cursor == '"' || *cursor == '\\') length += 2;
        else if (*cursor < 0x20) length += 6;
        else length++;
    }
    return length;
}

static char *write_escaped(char *destination, const char *value) {
    static const char hex[] = "0123456789abcdef";
    *destination++ = '"';
    for (const unsigned char *cursor = (const unsigned char *)value; *cursor; cursor++) {
        if (*cursor == '"' || *cursor == '\\') {
            *destination++ = '\\';
            *destination++ = (char)*cursor;
        } else if (*cursor < 0x20) {
            *destination++ = '\\'; *destination++ = 'u'; *destination++ = '0'; *destination++ = '0';
            *destination++ = hex[*cursor >> 4]; *destination++ = hex[*cursor & 0x0f];
        } else {
            *destination++ = (char)*cursor;
        }
    }
    *destination++ = '"';
    return destination;
}

size_t mosaic_plan_write(char *output, size_t output_capacity,
                         const char *steps_json, const char *output_step) {
    static const char prefix[] = "{\"steps\":";
    static const char middle[] = ",\"output\":";
    static const char suffix[] = "}";
    if (steps_json == NULL || output_step == NULL || steps_json[0] != '[') return 0;
    const size_t steps_length = strlen(steps_json);
    if (steps_length < 2 || steps_json[steps_length - 1] != ']') return 0;
    const size_t required = sizeof(prefix) - 1 + steps_length + sizeof(middle) - 1 +
                            escaped_length(output_step) + sizeof(suffix);
    if (output == NULL || output_capacity < required) return required;
    char *cursor = output;
    memcpy(cursor, prefix, sizeof(prefix) - 1); cursor += sizeof(prefix) - 1;
    memcpy(cursor, steps_json, steps_length); cursor += steps_length;
    memcpy(cursor, middle, sizeof(middle) - 1); cursor += sizeof(middle) - 1;
    cursor = write_escaped(cursor, output_step);
    memcpy(cursor, suffix, sizeof(suffix));
    return required;
}
