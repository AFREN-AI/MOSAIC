#include "mosaic_plan.h"

#include <assert.h>
#include <string.h>

int main(void) {
    const char *steps = "[{\"id\":\"first\",\"tool\":\"lookup\",\"arguments\":{}}]";
    const size_t required = mosaic_plan_write(NULL, 0, steps, "first\"step");
    assert(required > 0);
    char output[256];
    assert(mosaic_plan_write(output, sizeof(output), steps, "first\"step") == required);
    assert(strcmp(output, "{\"steps\":[{\"id\":\"first\",\"tool\":\"lookup\",\"arguments\":{}}],\"output\":\"first\\\"step\"}") == 0);
    assert(mosaic_plan_write(output, sizeof(output), "not-json", "step") == 0);
    return 0;
}
