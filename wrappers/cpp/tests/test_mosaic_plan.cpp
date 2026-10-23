#include "mosaic_plan.hpp"

#include <cassert>
#include <stdexcept>

int main() {
    mosaic::WorkflowBuilder builder;
    builder.add_step("lookup", "catalog.lookup", R"({"key":"a1"})")
           .add_step("format", "text.format", R"({"record":{"$ref":"lookup"}})");
    const auto plan = builder.to_json("format");
    assert(plan.find("\"output\":\"format\"") != std::string::npos);
    assert(plan.find("\"$ref\":\"lookup\"") != std::string::npos);

    bool rejected = false;
    try { static_cast<void>(builder.to_json("missing")); }
    catch (const std::invalid_argument&) { rejected = true; }
    assert(rejected);
}
