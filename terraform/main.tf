variable "image_uri" {
  description = "The full ECR image URI to deploy"
  type        = string
}

data "terraform_remote_state" "infra_vpc" {
  backend = "s3"
  config = {
    bucket = "terraform-state-bucket-d55fab12"
    key    = "prod/infra/vpc/terraform.tfstate"
    region = "ap-southeast-1"
  }
}

data "terraform_remote_state" "infra_ecs" {
  backend = "s3"
  config = {
    bucket = "terraform-state-bucket-d55fab12"
    key    = "prod/infra/ecs/terraform.tfstate"
    region = "ap-southeast-1"
  }
}

data "terraform_remote_state" "infra_iam" {
  backend = "s3"
  config = {
    bucket = "terraform-state-bucket-d55fab12"
    key    = "prod/infra/iam/terraform.tfstate"
    region = "ap-southeast-1"
  }
}

data "terraform_remote_state" "infra_api_gateway" {
  backend = "s3"
  config = {
    bucket = "terraform-state-bucket-d55fab12"
    key    = "prod/infra/api-gateway/terraform.tfstate"
    region = "ap-southeast-1"
  }
}

data "terraform_remote_state" "infra_s3" {
  backend = "s3"
  config = {
    bucket = "terraform-state-bucket-d55fab12"
    key    = "prod/infra/s3/terraform.tfstate"
    region = "ap-southeast-1"
  }
}

data "terraform_remote_state" "infra_dynamodb" {
  backend = "s3"
  config = {
    bucket = "terraform-state-bucket-d55fab12"
    key    = "prod/infra/dynamodb/terraform.tfstate"
    region = "ap-southeast-1"
  }
}

resource "aws_ecs_task_definition" "spot_submission_service_task" {
  family                   = "spot-submission-service-task"
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = "256"
  memory                   = "512"
  execution_role_arn       = data.terraform_remote_state.infra_iam.outputs.ecs_task_execution_role_arn
  task_role_arn            = data.terraform_remote_state.infra_iam.outputs.ecs_task_roles["spot-submission-service"].arn

  container_definitions = jsonencode([
    {
      name      = "spot-submission-service-container"
      image     = var.image_uri
      essential = true
      portMappings = [
        {
          containerPort = 80
          protocol      = "tcp"
        }
      ]
      environment = [
        {
          name  = "HTTP_PORTS"
          value = "80"
        },
        {
          name  = "SpotSubmissionStorage__BucketName"
          value = data.terraform_remote_state.infra_s3.outputs.bucket_name
        },
        {
          name  = "SpotSubmissionStorage__KeyPrefix"
          value = data.terraform_remote_state.infra_s3.outputs.photos_prefix
        },
        {
          name  = "SpotSubmissionStorage__UrlExpiryMinutes"
          value = "15"
        },
        {
          name  = "SpotSubmissionStorage__PublicBaseUrl"
          value = "https://${data.terraform_remote_state.infra_s3.outputs.cloudfront_domain_name}"
        },
        {
          name  = "DynamoDb"
          value = data.terraform_remote_state.infra_dynamodb.outputs.table_names["spot-submissions"]
        },
        {
          name  = "SpotsTable"
          value = data.terraform_remote_state.infra_dynamodb.outputs.table_names["spots"]
        }
      ]
      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = "makan-go/prod/spot-submission-service"
          "awslogs-region"        = "ap-southeast-1"
          "awslogs-stream-prefix" = "spot-submission-service"
        }
      }
    }
  ])
}

resource "aws_ecs_service" "spot_submission_service" {
  name            = "spot-submission-service"
  cluster         = data.terraform_remote_state.infra_ecs.outputs.aws_ecs_cluster_prod_id
  task_definition = aws_ecs_task_definition.spot_submission_service_task.arn
  desired_count   = 1
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = data.terraform_remote_state.infra_vpc.outputs.aws_subnet_ecs_subnet_ids
    assign_public_ip = true
    security_groups  = [data.terraform_remote_state.infra_vpc.outputs.aws_security_group_ecs_sg_id]
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.spot_submission_service_target_group.arn
    container_name   = "spot-submission-service-container"
    container_port   = 80
  }
}

resource "aws_lb" "spot_submission_service_network_load_balancer" {
  name               = "spot-submission-service-nlb"
  internal           = true
  load_balancer_type = "network"
  subnets            = data.terraform_remote_state.infra_vpc.outputs.aws_subnet_ecs_subnet_ids
}

resource "aws_lb_target_group" "spot_submission_service_target_group" {
  name        = "spot-submission-service-tg"
  port        = 80
  protocol    = "TCP"
  vpc_id      = data.terraform_remote_state.infra_vpc.outputs.aws_vpc_ecs_vpc_id
  target_type = "ip"

  health_check {
    protocol            = "TCP"
    port                = "traffic-port"
    healthy_threshold   = 2
    unhealthy_threshold = 2
    interval            = 10
    timeout             = 5
  }
}

resource "aws_lb_listener" "spot_submission_service_network_load_balancer_listener" {
  load_balancer_arn = aws_lb.spot_submission_service_network_load_balancer.arn
  port              = 80
  protocol          = "TCP"

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.spot_submission_service_target_group.arn
  }
}

resource "aws_apigatewayv2_integration" "spot_submission_service_integration" {
  api_id             = data.terraform_remote_state.infra_api_gateway.outputs.aws_apigatewayv2_api_makan_go_http_api_id
  integration_type   = "HTTP_PROXY"
  integration_uri    = aws_lb_listener.spot_submission_service_network_load_balancer_listener.arn
  connection_type    = "VPC_LINK"
  connection_id      = data.terraform_remote_state.infra_api_gateway.outputs.aws_apigatewayv2_vpc_link_ecs_vpc_link_id
  integration_method = "ANY"

  request_parameters = {
    "overwrite:path"           = "$request.path",
    "append:header.x-user-sub" = "$context.authorizer.claims.sub"
  }

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_apigatewayv2_route" "auth_route" {
  for_each = toset([
    "POST /spots/submissions/photos/presign",
    "GET /spots/submissions/health",
    "POST /spots/submissions",
    "GET /moderation/submissions",
    "POST /moderation/submissions/{id}/approve",
    "POST /moderation/submissions/{id}/reject"
  ])

  api_id    = data.terraform_remote_state.infra_api_gateway.outputs.aws_apigatewayv2_api_makan_go_http_api_id
  route_key = each.value
  target    = "integrations/${aws_apigatewayv2_integration.spot_submission_service_integration.id}"

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_cloudwatch_log_group" "spot_submission_service_log" {
  name              = "makan-go/prod/spot-submission-service"
  retention_in_days = 7
}
