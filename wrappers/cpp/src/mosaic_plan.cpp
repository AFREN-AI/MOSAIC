#include "mosaic_plan.hpp"
#include "mosaic_plan.h"

#include <stdexcept>
#include <utility>

namespace mosaic {
namespace {

std::string quote_json(const std::string& value) {
    std::string result = "\"";
    for (const unsigned char character : value) {
        if (character == '"' || character == '\\') {
            result.push_back('\\');
            result.push_back(static_cast<char>(character));
        } else if (character < 0x20) {
            constexpr char hex[] = "0123456789abcdef";
            result += "\\u00";
            result.push_back(hex[character >> 4]);
            result.push_back(hex[character & 0x0f]);
        } else {
            result.push_back(static_cast<char>(character));
        }
    }
    result.push_back('"');
    return result;
}

} // namespace

WorkflowBuilder& WorkflowBuilder::add_step(std::string id, std::string tool, std::string arguments_json) {
    if (id.empty() || tool.empty() || arguments_json.size() < 2 || arguments_json.front() != '{' || arguments_json.back() != '}') {
        throw std::invalid_argument("step id, tool and JSON object arguments are required");
    }
    for (const auto& current_id : step_ids_) {
        if (current_id == id) throw std::invalid_argument("duplicate step id: " + id);
    }
    step_json_.push_back("{\"id\":" + quote_json(id) + ",\"tool\":" + quote_json(tool) +
                         ",\"arguments\":" + arguments_json + "}");
    step_ids_.push_back(std::move(id));
    return *this;
}

std::string WorkflowBuilder::to_json(const std::string& output_step) const {
    if (step_json_.empty()) throw std::invalid_argument("workflow needs at least one step");
    bool found = false;
    std::string steps = "[";
    for (std::size_t index = 0; index < step_json_.size(); ++index) {
        if (index != 0) steps += ",";
        steps += step_json_[index];
        found = found || step_ids_[index] == output_step;
    }
    steps += "]";
    if (!found) throw std::invalid_argument("output must name a declared step");
    const auto required = mosaic_plan_write(nullptr, 0, steps.c_str(), output_step.c_str());
    std::string result(required, '\0');
    if (mosaic_plan_write(result.data(), result.size(), steps.c_str(), output_step.c_str()) == 0) {
        throw std::runtime_error("could not serialize workflow plan");
    }
    result.resize(required - 1);
    return result;
}

} // namespace mosaic
