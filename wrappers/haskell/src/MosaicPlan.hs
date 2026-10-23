{-# LANGUAGE OverloadedStrings #-}

-- | Construct shared MOSAIC plan JSON. Execution remains in the .NET host.
module MosaicPlan
  ( PlanBuilder
  , emptyPlan
  , addStep
  , reference
  , renderPlan
  ) where

import Data.Aeson (Object, Value (..), encode, object, (.=))
import qualified Data.Aeson.KeyMap as KeyMap
import qualified Data.ByteString.Lazy as LazyByteString
import Data.Text (Text)
import qualified Data.Text as Text
import qualified Data.Set as Set

data Step = Step Text Text Object
data PlanBuilder = PlanBuilder [Step]

emptyPlan :: PlanBuilder
emptyPlan = PlanBuilder []

reference :: Text -> Value
reference stepId = object ["$ref" .= stepId]

addStep :: Text -> Text -> Object -> PlanBuilder -> Either Text PlanBuilder
addStep identifier tool arguments (PlanBuilder steps)
  | Text.null (Text.strip identifier) || Text.null (Text.strip tool) = Left "step id and tool are required"
  | any (sameIdentifier identifier) steps = Left "duplicate step id"
  | otherwise = do
      validateReferences (Set.fromList (map stepIdentifier steps)) (Object arguments)
      Right (PlanBuilder (steps ++ [Step identifier tool arguments]))

renderPlan :: PlanBuilder -> Text -> Either Text LazyByteString.ByteString
renderPlan (PlanBuilder []) _ = Left "workflow needs at least one step"
renderPlan (PlanBuilder steps) output
  | not (any (sameIdentifier output) steps) = Left "output must name a declared step"
  | otherwise = Right (encode (object ["steps" .= map encodeStep steps, "output" .= output]))

sameIdentifier :: Text -> Step -> Bool
sameIdentifier identifier (Step existing _ _) = identifier == existing

stepIdentifier :: Step -> Text
stepIdentifier (Step identifier _ _) = identifier

encodeStep :: Step -> Value
encodeStep (Step identifier tool arguments) = object ["id" .= identifier, "tool" .= tool, "arguments" .= Object arguments]

validateReferences :: Set.Set Text -> Value -> Either Text ()
validateReferences earlier (Object values)
  | KeyMap.size values == 1, Just rawReference <- KeyMap.lookup "$ref" values =
      case rawReference of
        String identifier | Set.member identifier earlier -> Right ()
        _ -> Left "references must point to an earlier step"
  | otherwise = mapM_ (validateReferences earlier) (KeyMap.elems values)
validateReferences earlier (Array values) = mapM_ (validateReferences earlier) values
validateReferences _ _ = Right ()
