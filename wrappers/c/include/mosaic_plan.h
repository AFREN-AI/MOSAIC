#ifndef MOSAIC_PLAN_H
#define MOSAIC_PLAN_H

#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Writes a complete plan with caller-supplied JSON steps and escaped output id.
 * Returns bytes required including the terminating NUL, or 0 for invalid input.
 * A null/short output buffer is permitted to query the required size. */
size_t mosaic_plan_write(char *output, size_t output_capacity,
                         const char *steps_json, const char *output_step);

#ifdef __cplusplus
}
#endif

#endif
