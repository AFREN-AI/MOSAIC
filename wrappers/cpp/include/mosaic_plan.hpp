#ifndef MOSAIC_PLAN_HPP
#define MOSAIC_PLAN_HPP

#include <string>
#include <vector>

namespace mosaic {

// Builds the shared MOSAIC plan JSON contract. The .NET host executes it.
class WorkflowBuilder final {
public:
    WorkflowBuilder& add_step(std::string id, std::string tool, std::string arguments_json);
    [[nodiscard]] std::string to_json(const std::string& output_step) const;

private:
    std::vector<std::string> step_json_;
    std::vector<std::string> step_ids_;
};

} // namespace mosaic

#endif
