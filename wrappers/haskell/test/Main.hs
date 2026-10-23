{-# LANGUAGE OverloadedStrings #-}

module Main (main) where

import Data.Aeson (Value (..), decode)
import qualified Data.Aeson.KeyMap as KeyMap
import qualified Data.ByteString.Lazy as LazyByteString
import qualified Data.Text as Text
import MosaicPlan (addStep, emptyPlan, reference, renderPlan)
import System.Exit (exitFailure)

main :: IO ()
main = do
  let first = addStep "lookup" "catalog.lookup" (KeyMap.fromList [("key", String "a1")]) emptyPlan
  let second = first >>= addStep "format" "text.format" (KeyMap.fromList [("record", reference "lookup")])
  case second >>= (`renderPlan` "format") of
    Left problem -> failWith (Text.unpack problem)
    Right bytes -> checkContract bytes
  case addStep "first" "tool" (KeyMap.fromList [("value", reference "future")]) emptyPlan of
    Left _ -> pure ()
    Right _ -> failWith "forward reference was accepted"

checkContract :: LazyByteString.ByteString -> IO ()
checkContract bytes = case decode bytes of
  Just (Object plan) -> case KeyMap.lookup "output" plan of
    Just (String "format") -> pure ()
    _ -> failWith "incorrect output step"
  _ -> failWith "plan is not valid JSON object"

failWith :: String -> IO a
failWith message = putStrLn message >> exitFailure
