cmake_minimum_required(VERSION 3.20)
project(mosaic_plan C)

add_library(mosaic_plan src/mosaic_plan.c)
target_include_directories(mosaic_plan PUBLIC include)
target_compile_features(mosaic_plan PUBLIC c_std_11)

add_executable(test_mosaic_plan tests/test_mosaic_plan.c)
target_link_libraries(test_mosaic_plan PRIVATE mosaic_plan)
enable_testing()
add_test(NAME mosaic_plan_contract COMMAND test_mosaic_plan)
